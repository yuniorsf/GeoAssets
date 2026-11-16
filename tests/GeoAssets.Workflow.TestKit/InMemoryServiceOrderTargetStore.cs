using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Dictionary-backed <see cref="IServiceOrderTargetReader"/> + <see cref="IServiceOrderTargetWriter"/>
/// for tests that need a working target store without a database. It passes the same
/// <see cref="ServiceOrderTargetStoreContractTests"/> the EF Core store does, so a service
/// tested against it behaves the same against the real one.
///
/// Orders are not stored here: the store asks <paramref name="orders"/> (any
/// <see cref="IServiceOrderReader"/>, typically the test's own fake order repository) whether an
/// order exists, which organization owns it and what state it is in. A target counts as
/// released as soon as its order is in a terminal state — there is nothing to stamp, so unlike
/// the EF store a reopened order's targets count as open again.
/// </summary>
/// <param name="orders">Where orders are looked up.</param>
/// <param name="orderTypes">Source of each order type's exclusivity and terminal states; the default order graph and no exclusivity when omitted.</param>
/// <param name="tenantContext">The caller's organization, used by <see cref="FindOpenUsagesAsync"/> only when the excluded order does not exist.</param>
public sealed class InMemoryServiceOrderTargetStore(
    IServiceOrderReader orders,
    OrderTypeRegistry? orderTypes = null,
    ITenantContext? tenantContext = null) : IServiceOrderTargetReader, IServiceOrderTargetWriter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ServiceOrderTarget> _targets = new(StringComparer.Ordinal);
    private readonly IServiceOrderReader _orders = orders ?? throw new ArgumentNullException(nameof(orders));
    private readonly ITenantContext _tenantContext = tenantContext ?? NullTenantContext.Instance;

    /// <summary>Every stored target, in no particular order — for assertions.</summary>
    public IReadOnlyList<ServiceOrderTarget> Snapshot()
    {
        lock (_gate) return [.. _targets.Values];
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        List<ServiceOrderTarget> matches;
        lock (_gate)
        {
            matches = _targets.Values
                .Where(t => string.Equals(t.OrderId, query.OrderId, StringComparison.Ordinal))
                .Where(t => query.Status is null || string.Equals(t.Status, query.Status, StringComparison.Ordinal))
                .Where(t => query.ConflictLevel is null || t.ConflictLevel == query.ConflictLevel)
                .ToList();
        }

        matches.Sort(Comparison(OrderTargetSortKeys.Resolve(query.SortBy), query.Descending));

        return Task.FromResult(new PagedResult<ServiceOrderTarget>
        {
            Items = matches.Skip(query.Skip).Take(query.PageSize).ToList(),
            TotalCount = matches.Count,
        });
    }

    public Task<int> CountAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            return Task.FromResult(_targets.Values.Count(t => string.Equals(t.OrderId, orderId, StringComparison.Ordinal)));
    }

    public async Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(excludingOrderId);
        ct.ThrowIfCancellationRequested();

        var wanted = featureIds.Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0) return [];

        var excluded = await _orders.GetByIdAsync(excludingOrderId, ct).ConfigureAwait(false);
        var organizationId = excluded?.OrganizationId ?? _tenantContext.CurrentOrganizationId;

        List<ServiceOrderTarget> held;
        lock (_gate)
        {
            held = _targets.Values
                .Where(t => wanted.Contains(t.FeatureId) && !string.Equals(t.OrderId, excludingOrderId, StringComparison.Ordinal))
                .ToList();
        }

        var usages = new List<OrderTargetUsage>();
        foreach (var byOrder in held.GroupBy(t => t.OrderId, StringComparer.Ordinal))
        {
            var holder = await _orders.GetByIdAsync(byOrder.Key, ct).ConfigureAwait(false);
            if (holder is null || holder.OrganizationId != organizationId) continue;

            var orderType = orderTypes?.Find(holder.OrderTypeId);
            if (ServiceOrderTransitions.IsTerminalState(orderType, holder.Status)) continue;

            usages.AddRange(byOrder.Select(t => new OrderTargetUsage(
                t.FeatureId,
                holder.Id,
                holder.OrderTypeId,
                holder.Status,
                holder.ParentOrderId,
                orderType?.TargetPolicy?.Exclusivity ?? OrderTargetExclusivity.None)));
        }

        return usages;
    }

    public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();

        List<(double Longitude, double Latitude)> positions;
        lock (_gate)
        {
            positions = _targets.Values
                .Where(t => string.Equals(t.OrderId, orderId, StringComparison.Ordinal))
                .Where(t => t.Longitude is not null && t.Latitude is not null)
                .Select(t => (t.Longitude!.Value, t.Latitude!.Value))
                .ToList();
        }

        return Task.FromResult(positions.Count == 0
            ? null
            : new OrderTargetExtent(
                positions.Min(p => p.Longitude),
                positions.Min(p => p.Latitude),
                positions.Max(p => p.Longitude),
                positions.Max(p => p.Latitude)));
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(targets);
        ct.ThrowIfCancellationRequested();

        if (targets.Count == 0) return 0;

        if (targets.Any(target => target is null || !string.Equals(target.OrderId, orderId, StringComparison.Ordinal)))
            throw new ArgumentException($"Every target must belong to ServiceOrder '{orderId}'.", nameof(targets));

        var order = await _orders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

        if (targets.Any(target => target.OrganizationId != order.OrganizationId))
            throw new ArgumentException($"Every target must carry the organization of ServiceOrder '{orderId}'.", nameof(targets));

        lock (_gate)
        {
            var heldFeatureIds = _targets.Values
                .Where(t => string.Equals(t.OrderId, orderId, StringComparison.Ordinal))
                .Select(t => t.FeatureId)
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var target in targets)
            {
                if (!heldFeatureIds.Add(target.FeatureId)) continue;

                _targets.Add(target.Id, target with { ActivityIds = [.. target.ActivityIds] });
                added++;
            }

            return added;
        }
    }

    public Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_targets.TryGetValue(target.Id, out var stored) || !string.Equals(stored.OrderId, target.OrderId, StringComparison.Ordinal))
                throw new KeyNotFoundException($"ServiceOrderTarget '{target.Id}' not found in ServiceOrder '{target.OrderId}'.");

            if (stored.Version != target.Version)
                throw new ServiceOrderConcurrencyException(target.OrderId);

            _targets[target.Id] = stored with
            {
                ActivityIds = [.. target.ActivityIds],
                Status = target.Status,
                ConflictLevel = target.ConflictLevel,
                Note = target.Note,
                Version = checked(target.Version + 1),
            };
        }

        return Task.CompletedTask;
    }

    public Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(targetIds);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var removed = 0;
            foreach (var targetId in targetIds.Where(id => id is not null).Distinct(StringComparer.Ordinal))
            {
                if (_targets.TryGetValue(targetId, out var stored)
                    && string.Equals(stored.OrderId, orderId, StringComparison.Ordinal)
                    && _targets.Remove(targetId))
                {
                    removed++;
                }
            }

            return Task.FromResult(removed);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Comparison<ServiceOrderTarget> Comparison(string sortKey, bool descending)
    {
        Comparison<ServiceOrderTarget> byKey = sortKey switch
        {
            // A null alias compares greater than any text, so it lands last ascending and —
            // once the direction is flipped below — first descending.
            OrderTargetSortKeys.Alias => (a, b) => (a.Alias, b.Alias) switch
            {
                (null, null) => 0,
                (null, _) => 1,
                (_, null) => -1,
                var (x, y) => string.CompareOrdinal(x, y),
            },
            OrderTargetSortKeys.Status => (a, b) => string.CompareOrdinal(a.Status, b.Status),
            OrderTargetSortKeys.ConflictLevel => (a, b) => a.ConflictLevel.CompareTo(b.ConflictLevel),
            OrderTargetSortKeys.AddedAt => (a, b) => a.AddedAt.CompareTo(b.AddedAt),
            _ => (a, b) => string.CompareOrdinal(a.FeatureId, b.FeatureId),
        };

        return (a, b) =>
        {
            var order = byKey(a, b);
            if (descending) order = -order;
            return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
        };
    }
}
