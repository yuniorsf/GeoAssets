using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Dictionary-backed <see cref="IOccupancyChangeSetReader"/> + <see cref="IOccupancyChangeSetWriter"/>
/// (XD01-315) for tests that need a working store of staged occupancy changes without a
/// database. It passes the same <see cref="OccupancyChangeSetStoreContractTests"/> the EF Core
/// store does, so a service tested against it behaves the same against the real one.
///
/// Like the real store it is scoped to one organization (the pole owner), given by the tenant
/// context; <see cref="ForOrganization"/> hands out another view over the <b>same</b> data.
///
/// It stores neither orders nor targets: it asks the given target and order readers. What the
/// database does by cascade and what closing an order does in the same write, it does on the
/// next access instead — a set whose target or order is gone is dropped, and a set still staged
/// on an order that is now in a terminal state becomes discarded, stamped with the clock.
/// </summary>
public sealed class InMemoryOccupancyChangeSetStore : IOccupancyChangeSetReader, IOccupancyChangeSetWriter
{
    private readonly State _state;
    private readonly ITenantContext _tenantContext;

    /// <param name="targets">Where order targets are looked up.</param>
    /// <param name="orders">Where orders are looked up, to know which are closed.</param>
    /// <param name="orderTypes">Source of each order type's terminal states; the default order graph when omitted.</param>
    /// <param name="tenantContext">The pole owner this view is scoped to; <see cref="Guid.Empty"/> when omitted.</param>
    /// <param name="timeProvider">Clock for the instant a set is discarded; the system clock when omitted.</param>
    public InMemoryOccupancyChangeSetStore(
        IServiceOrderTargetReader targets,
        IServiceOrderReader orders,
        OrderTypeRegistry? orderTypes = null,
        ITenantContext? tenantContext = null,
        TimeProvider? timeProvider = null)
        : this(
            new State(
                targets ?? throw new ArgumentNullException(nameof(targets)),
                orders ?? throw new ArgumentNullException(nameof(orders)),
                orderTypes,
                timeProvider ?? TimeProvider.System),
            tenantContext ?? NullTenantContext.Instance)
    {
    }

    private InMemoryOccupancyChangeSetStore(State state, ITenantContext tenantContext)
    {
        _state = state;
        _tenantContext = tenantContext;
    }

    /// <summary>A view over the same stored data, scoped to <paramref name="organizationId"/>.</summary>
    public InMemoryOccupancyChangeSetStore ForOrganization(Guid organizationId) =>
        new(_state, new FixedTenant(organizationId));

    /// <summary>Every stored set of every organization, in no particular order — for assertions.</summary>
    public IReadOnlyList<OccupancyChangeSet> Snapshot()
    {
        lock (_state.Gate) return [.. _state.Sets.Values];
    }

    /// <summary>
    /// Marks a target as released although its order is open — the state the EF store keeps for
    /// a target of an order that closed and was reopened — so a test can reach the
    /// "released target" refusal without closing the order.
    /// </summary>
    public void MarkReleased(string targetId)
    {
        lock (_state.Gate) _state.ReleasedTargetIds.Add(targetId);
    }

    /// <summary>
    /// Stores a set exactly as given, in whatever status, past every check — how a test puts a
    /// committed set on file before the slice that commits exists.
    /// </summary>
    public void Seed(OccupancyChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        lock (_state.Gate) _state.Sets[changeSet.Id] = changeSet;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<OccupancyChangeSet?> GetStagedAsync(string orderId, string targetId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ct.ThrowIfCancellationRequested();

        await SettleAsync(orderId, ct).ConfigureAwait(false);
        lock (_state.Gate)
        {
            return Mine(orderId).FirstOrDefault(set =>
                set.Status == OccupancyChangeSetStatus.Staged && string.Equals(set.TargetId, targetId, StringComparison.Ordinal));
        }
    }

    public async Task<PagedResult<OccupancyChangeSet>> GetPageAsync(OccupancyChangeSetQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        await SettleAsync(query.OrderId, ct).ConfigureAwait(false);
        List<OccupancyChangeSet> matches;
        lock (_state.Gate)
        {
            matches = Mine(query.OrderId)
                .Where(set => query.Status is null || set.Status == query.Status)
                .OrderBy(set => set.PoleFeatureId, StringComparer.Ordinal)
                .ThenBy(set => set.Id.ToString("D"), StringComparer.Ordinal)
                .ToList();
        }

        return new PagedResult<OccupancyChangeSet>
        {
            Items = matches.Skip(query.Skip).Take(query.PageSize).ToList(),
            TotalCount = matches.Count,
        };
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<OccupancyChangeSet> StageAsync(OccupancyChangeSet changeSet, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ct.ThrowIfCancellationRequested();

        if (changeSet.Status != OccupancyChangeSetStatus.Staged)
            throw new ArgumentException($"Only a staged change set can be staged; this one is {changeSet.Status}.", nameof(changeSet));
        if (changeSet.PoleOwnerOrganizationId != _tenantContext.CurrentOrganizationId)
            throw new ArgumentException("The change set belongs to another organization than the store's.", nameof(changeSet));

        await SettleAsync(changeSet.OrderId, ct).ConfigureAwait(false);
        var target = await _state.Targets.GetByIdAsync(changeSet.OrderId, changeSet.TargetId, ct).ConfigureAwait(false);
        if (target is null
            || target.OrganizationId != changeSet.PoleOwnerOrganizationId
            || !string.Equals(target.FeatureId, changeSet.PoleFeatureId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException(
                $"Service order '{changeSet.OrderId}' has no target '{changeSet.TargetId}' for pole '{changeSet.PoleFeatureId}'.");
        }

        var isClosed = await IsClosedAsync(changeSet.OrderId, ct).ConfigureAwait(false);
        lock (_state.Gate)
        {
            if (isClosed || _state.ReleasedTargetIds.Contains(changeSet.TargetId))
                throw new OccupancyTargetReleasedException(changeSet.OrderId, changeSet.TargetId);

            if (_state.Sets.TryGetValue(changeSet.Id, out var sameId) && sameId.Status != OccupancyChangeSetStatus.Staged)
                throw new ArgumentException($"Change set '{changeSet.Id}' is already on file and no longer staged.", nameof(changeSet));

            foreach (var replaced in Mine(changeSet.OrderId)
                .Where(set => set.Status == OccupancyChangeSetStatus.Staged && string.Equals(set.TargetId, changeSet.TargetId, StringComparison.Ordinal))
                .ToList())
            {
                _state.Sets.Remove(replaced.Id);
            }

            _state.Sets[changeSet.Id] = changeSet;
            return changeSet;
        }
    }

    public async Task<bool> DiscardAsync(string orderId, string targetId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ct.ThrowIfCancellationRequested();

        await SettleAsync(orderId, ct).ConfigureAwait(false);
        return Discard(orderId, set => string.Equals(set.TargetId, targetId, StringComparison.Ordinal)) > 0;
    }

    public async Task<int> DiscardAllAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();

        await SettleAsync(orderId, ct).ConfigureAwait(false);
        return Discard(orderId, _ => true);
    }

    public async Task<bool> MarkCommittedAsync(OccupancyChangeSet committed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ct.ThrowIfCancellationRequested();

        if (committed.Status != OccupancyChangeSetStatus.Committed)
            throw new ArgumentException($"Only a committed change set can be recorded as committed; this one is {committed.Status}.", nameof(committed));
        if (committed.PoleOwnerOrganizationId != _tenantContext.CurrentOrganizationId)
            throw new ArgumentException("The change set belongs to another organization than the store's.", nameof(committed));

        await SettleAsync(committed.OrderId, ct).ConfigureAwait(false);
        lock (_state.Gate)
        {
            if (!_state.Sets.TryGetValue(committed.Id, out var current) || current.Status != OccupancyChangeSetStatus.Staged)
                return false;

            _state.Sets[committed.Id] = current.Commit(committed.ClosedAt!.Value);
            return true;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The caller's organization's sets of one order. Call under the gate.</summary>
    private IEnumerable<OccupancyChangeSet> Mine(string orderId) => _state.Sets.Values.Where(set =>
        set.PoleOwnerOrganizationId == _tenantContext.CurrentOrganizationId
        && string.Equals(set.OrderId, orderId, StringComparison.Ordinal));

    private int Discard(string orderId, Func<OccupancyChangeSet, bool> which)
    {
        var now = _state.TimeProvider.GetUtcNow();
        lock (_state.Gate)
        {
            var staged = Mine(orderId).Where(set => set.Status == OccupancyChangeSetStatus.Staged && which(set)).ToList();
            foreach (var set in staged)
                _state.Sets[set.Id] = set.Discard(now);

            return staged.Count;
        }
    }

    private async Task<bool> IsClosedAsync(string orderId, CancellationToken ct)
    {
        var order = await _state.Orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        return order is not null && ServiceOrderTransitions.IsTerminalState(_state.OrderTypes?.Find(order.OrderTypeId), order.Status);
    }

    /// <summary>
    /// Brings every organization's sets of one order to where a database would already have
    /// them: gone with their target or order, discarded once the order has closed.
    /// </summary>
    private async Task SettleAsync(string orderId, CancellationToken ct)
    {
        List<OccupancyChangeSet> sets;
        lock (_state.Gate)
            sets = [.. _state.Sets.Values.Where(set => string.Equals(set.OrderId, orderId, StringComparison.Ordinal))];
        if (sets.Count == 0) return;

        var order = await _state.Orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        var isClosed = order is not null && ServiceOrderTransitions.IsTerminalState(_state.OrderTypes?.Find(order.OrderTypeId), order.Status);
        var now = _state.TimeProvider.GetUtcNow();

        foreach (var set in sets)
        {
            var targetExists = order is not null
                && await _state.Targets.GetByIdAsync(orderId, set.TargetId, ct).ConfigureAwait(false) is not null;

            lock (_state.Gate)
            {
                if (!_state.Sets.TryGetValue(set.Id, out var current)) continue;

                if (!targetExists) _state.Sets.Remove(set.Id);
                else if (isClosed && current.Status == OccupancyChangeSetStatus.Staged) _state.Sets[set.Id] = current.Discard(now);
            }
        }
    }

    private sealed class State(
        IServiceOrderTargetReader targets, IServiceOrderReader orders, OrderTypeRegistry? orderTypes, TimeProvider timeProvider)
    {
        public Lock Gate { get; } = new();

        public Dictionary<Guid, OccupancyChangeSet> Sets { get; } = [];

        public HashSet<string> ReleasedTargetIds { get; } = new(StringComparer.Ordinal);

        public IServiceOrderTargetReader Targets { get; } = targets;

        public IServiceOrderReader Orders { get; } = orders;

        public OrderTypeRegistry? OrderTypes { get; } = orderTypes;

        public TimeProvider TimeProvider { get; } = timeProvider;
    }

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
