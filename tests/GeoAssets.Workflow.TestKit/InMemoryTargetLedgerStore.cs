using GeoAssets.Core.Models;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// An in-memory <see cref="ITargetLedgerStore"/> that follows the port's documented contract (remote verticals' rows
/// only): the same member semantics the EF store is held to, so service- and route-level tests can run without a database.
/// </summary>
public sealed class InMemoryTargetLedgerStore : ITargetLedgerStore
{
    /// <summary>Counts how often a reservation took the feature locks (one writer at a time here, so no lock is needed).</summary>
    public int LockCalls { get; private set; }

    /// <summary>Runs when a reservation takes the feature locks — a test can commit a competing reservation there.</summary>
    public Func<CancellationToken, Task>? OnLockFeatures { get; set; }

    public Task LockFeaturesAsync(IReadOnlyCollection<string> featureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        LockCalls++;
        return OnLockFeatures?.Invoke(ct) ?? Task.CompletedTask;
    }

    private readonly Lock _gate = new();
    private readonly List<Row> _rows = [];

    /// <summary>The number of calls made to any member, to prove a path never reached the store.</summary>
    public int Calls { get; private set; }

    /// <summary>Every stored row, released ones included.</summary>
    public IReadOnlyList<(ServiceOrderTarget Target, DateTime? ReleasedAt)> Rows
    {
        get
        {
            lock (_gate) return [.. _rows.Select(r => (r.Target, r.ReleasedAt))];
        }
    }

    public Task<IReadOnlyList<TargetLedgerUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, TargetLedgerKey excluding, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Calls++;
            IReadOnlyList<TargetLedgerUsage> usages =
            [
                .. _rows
                    .Where(r => r.ReleasedAt is null && featureIds.Contains(r.Target.FeatureId, StringComparer.Ordinal))
                    .Where(r => !IsKey(r, excluding))
                    .Select(r => new TargetLedgerUsage(
                        new OrderTargetUsage(r.Target.FeatureId, r.Target.OrderId, r.OrderTypeId, string.Empty, null, r.Exclusivity, r.Target.VerticalId),
                        r.Target.OrganizationId)),
            ];
            return Task.FromResult(usages);
        }
    }

    public Task<Guid?> FindOrganizationAsync(TargetLedgerKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Calls++;
            return Task.FromResult<Guid?>(Of(key).Select(r => (Guid?)r.Target.OrganizationId).FirstOrDefault());
        }
    }

    public Task<PagedResult<TargetLedgerEntry>> GetPageAsync(TargetLedgerKey key, Guid organizationId, int skip, int take, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Calls++;
            var matches = Of(key).Where(r => r.Target.OrganizationId == organizationId)
                .OrderBy(r => r.Target.FeatureId, StringComparer.Ordinal).ThenBy(r => r.Target.Id, StringComparer.Ordinal).ToList();
            return Task.FromResult(new PagedResult<TargetLedgerEntry>
            {
                Items = [.. matches.Skip(skip).Take(take).Select(r => new TargetLedgerEntry(r.Target, r.ReleasedAt))],
                TotalCount = matches.Count,
            });
        }
    }

    public Task<IReadOnlyList<ServiceOrderTarget>> ReserveAsync(
        TargetLedgerOrder order, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(targets);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Calls++;
            if (targets.Any(t => !string.Equals(t.VerticalId, order.VerticalId, StringComparison.Ordinal) || !string.Equals(t.OrderId, order.OrderId, StringComparison.Ordinal) || t.OrganizationId != order.OrganizationId))
                throw new ArgumentException("Every target must belong to the order.", nameof(targets));

            var inserted = new List<ServiceOrderTarget>();
            foreach (var target in targets)
            {
                if (Of(order.Key).Any(r => string.Equals(r.Target.FeatureId, target.FeatureId, StringComparison.Ordinal))) continue;
                _rows.Add(new Row(target, null, order.OrderTypeId, order.Policy.Exclusivity));
                inserted.Add(target);
            }

            return Task.FromResult<IReadOnlyList<ServiceOrderTarget>>(inserted);
        }
    }

    public Task<IReadOnlyList<ServiceOrderTarget>> ReleaseAsync(
        TargetLedgerKey key, Guid? organizationId, IReadOnlyCollection<string>? targetIds, DateTimeOffset releasedAt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ServiceOrderTarget>>(Change(key, organizationId, targetIds, _ => true, row => row with { ReleasedAt = releasedAt.UtcDateTime }));
        }
    }

    public Task<IReadOnlyList<ServiceOrderTarget>> CommitAsync(TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ServiceOrderTarget>>(Change(
                key,
                organizationId,
                targetIds,
                row => row.Target.Status is OrderTargetStatus.Pending or OrderTargetStatus.InProgress,
                row => row with { Target = row.Target with { Status = OrderTargetStatus.Done, Version = row.Target.Version + 1 } }));
        }
    }

    private List<ServiceOrderTarget> Change(
        TargetLedgerKey key, Guid? organizationId, IReadOnlyCollection<string>? targetIds, Func<Row, bool> applies, Func<Row, Row> change)
    {
        var changed = new List<ServiceOrderTarget>();
        foreach (var row in Of(key).Where(r => r.ReleasedAt is null && applies(r)).ToList())
        {
            if (organizationId is { } organization && row.Target.OrganizationId != organization) continue;
            if (targetIds is not null && !targetIds.Contains(row.Target.Id, StringComparer.Ordinal)) continue;

            var updated = change(row);
            _rows[_rows.IndexOf(row)] = updated;
            changed.Add(updated.Target);
        }

        return changed;
    }

    private IEnumerable<Row> Of(TargetLedgerKey key) =>
        _rows.Where(r => IsKey(r, key));

    private static bool IsKey(Row row, TargetLedgerKey key) =>
        string.Equals(row.Target.VerticalId, key.VerticalId, StringComparison.Ordinal)
        && string.Equals(row.Target.OrderId, key.OrderId, StringComparison.Ordinal);

    private sealed record Row(ServiceOrderTarget Target, DateTime? ReleasedAt, string OrderTypeId, OrderTargetExclusivity Exclusivity);
}
