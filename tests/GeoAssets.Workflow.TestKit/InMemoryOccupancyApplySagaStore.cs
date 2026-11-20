using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Dictionary-backed <see cref="IOccupancyApplySagaStore"/> and <see cref="IOccupancyApplySagaMonitor"/> (XD01-383) for
/// in-memory fixtures. It keeps the sagas and their poles and, when it is given the change-set and pole stores a fixture
/// shares with the approval, does what the EF store does in the same unit of work: moves the saga's change sets from staged
/// to pending and on to committed or reverted, and restores the poles' baselines on a revert — leaving, and naming, a pole
/// moved on since (XD01-392). Without them it keeps state only.
/// </summary>
public sealed class InMemoryOccupancyApplySagaStore : IOccupancyApplySagaStore, IOccupancyApplySagaMonitor
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, OccupancyApplySaga> _sagas = [];
    private readonly Dictionary<Guid, IReadOnlyList<OccupancyApplySagaPole>> _poles = [];
    private readonly Dictionary<Guid, Guid[]> _changeSetIds = [];
    private readonly InMemoryOccupancyChangeSetStore? _changeSetStore;
    private readonly InMemoryPoleOccupancyStore? _poleStore;

    /// <param name="changeSets">The change-set store the approval stages in; its sets are moved with the saga. Optional.</param>
    /// <param name="poles">The occupancy store the approval writes; its poles are restored on a revert. Optional.</param>
    public InMemoryOccupancyApplySagaStore(InMemoryOccupancyChangeSetStore? changeSets = null, InMemoryPoleOccupancyStore? poles = null)
    {
        _changeSetStore = changeSets;
        _poleStore = poles;
    }

    /// <summary>Every saga on file.</summary>
    public IReadOnlyList<OccupancyApplySaga> All
    {
        get
        {
            lock (_gate) return [.. _sagas.Values];
        }
    }

    public Task<bool> BeginAsync(
        OccupancyApplySaga saga, IReadOnlyCollection<OccupancyApplySagaPole> poles, IReadOnlyCollection<Guid> changeSetIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(poles);
        lock (_gate)
        {
            var ids = changeSetIds.Distinct().ToArray();
            if (_sagas.ContainsKey(saga.BatchId)) return Task.FromResult(false);
            if (_changeSetStore is not null)
            {
                var staged = _changeSetStore.Snapshot().ToDictionary(set => set.Id);
                if (ids.Any(id => !staged.TryGetValue(id, out var set) || set.Status != OccupancyChangeSetStatus.Staged))
                    return Task.FromResult(false);

                foreach (var id in ids)
                    _changeSetStore.Seed(Settled(staged[id], OccupancyChangeSetStatus.Pending, saga.RequestedAt));
            }

            _sagas[saga.BatchId] = saga;
            _poles[saga.BatchId] = [.. poles];
            _changeSetIds[saga.BatchId] = ids;
            return Task.FromResult(true);
        }
    }

    public Task<OccupancyApplySaga?> FindAsync(Guid batchId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_sagas.GetValueOrDefault(batchId));
    }

    public Task<OccupancyApplySaga?> FindLatestForOrderAsync(string orderId, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_sagas.Values
                .Where(s => string.Equals(s.OrderId, orderId, StringComparison.Ordinal))
                .OrderByDescending(s => s.Attempt)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<string>> FindPendingPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureIds);
        lock (_gate)
        {
            IReadOnlyList<string> held = [.. _sagas.Values
                .Where(s => s.State == OccupancyApplySagaState.Pending)
                .SelectMany(s => _poles.GetValueOrDefault(s.BatchId) ?? [])
                .Select(p => p.PoleFeatureId)
                .Where(id => poleFeatureIds.Contains(id, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
            return Task.FromResult(held);
        }
    }

    public Task<IReadOnlyList<OccupancyApplySagaPole>> GetPolesAsync(Guid batchId, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(_poles.GetValueOrDefault(batchId) ?? []);
    }

    public Task<OccupancyApplySagaCompletion> CompleteAsync(OccupancyApplySaga completed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(completed);
        lock (_gate)
        {
            if (!_sagas.TryGetValue(completed.BatchId, out var stored) || stored.State != OccupancyApplySagaState.Pending)
                return Task.FromResult(OccupancyApplySagaCompletion.NotPending);

            var reverted = completed.State == OccupancyApplySagaState.Reverted;
            List<string> unrestored = [];
            if (reverted && _poleStore is not null)
            {
                var owner = _poleStore.ForOrganization(completed.OrganizationId);
                foreach (var pole in (_poles.GetValueOrDefault(completed.BatchId) ?? []).OrderBy(p => p.PoleFeatureId, StringComparer.Ordinal))
                {
                    if (owner.TryRestoreEntries(pole.PoleFeatureId, pole.AppliedVersion, pole.BaselineEntries) is null)
                        unrestored.Add(pole.PoleFeatureId);
                }
            }

            _sagas[completed.BatchId] = completed with { UnrestoredFeatureIds = unrestored };

            if (_changeSetStore is not null)
            {
                var settled = reverted ? OccupancyChangeSetStatus.Reverted : OccupancyChangeSetStatus.Committed;
                var ids = _changeSetIds.GetValueOrDefault(completed.BatchId) ?? [];
                foreach (var set in _changeSetStore.Snapshot().Where(set => ids.Contains(set.Id) && set.Status == OccupancyChangeSetStatus.Pending))
                    _changeSetStore.Seed(Settled(set, settled, completed.CompletedAt!.Value));
            }

            return Task.FromResult(unrestored.Count == 0
                ? OccupancyApplySagaCompletion.Done
                : new OccupancyApplySagaCompletion(true, unrestored));
        }
    }

    public Task<bool> RecordRetryAsync(Guid batchId, DateTimeOffset retriedAt, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_sagas.TryGetValue(batchId, out var stored) || stored.State != OccupancyApplySagaState.Pending)
                return Task.FromResult(false);
            _sagas[batchId] = stored with { RetryCount = stored.RetryCount + 1, LastRequestedAt = retriedAt, TimedOutAt = null };
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<OccupancyApplySaga>> MarkTimedOutAsync(DateTimeOffset staleBefore, DateTimeOffset now, int max, CancellationToken ct)
    {
        lock (_gate)
        {
            var stale = _sagas.Values
                .Where(s => s.State == OccupancyApplySagaState.Pending && s.TimedOutAt is null && s.LastRequestedAt < staleBefore)
                .OrderBy(s => s.LastRequestedAt)
                .Take(max)
                .Select(s => s with { TimedOutAt = now })
                .ToList();
            foreach (var saga in stale)
                _sagas[saga.BatchId] = saga;
            return Task.FromResult<IReadOnlyList<OccupancyApplySaga>>(stale);
        }
    }

    public Task<(int Pending, DateTimeOffset? OldestLastRequestedAt)> GetBacklogAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            var pending = _sagas.Values.Where(s => s.State == OccupancyApplySagaState.Pending).ToList();
            return Task.FromResult<(int, DateTimeOffset?)>((pending.Count, pending.Count == 0 ? null : pending.Min(s => s.LastRequestedAt)));
        }
    }

    private static OccupancyChangeSet Settled(OccupancyChangeSet set, OccupancyChangeSetStatus status, DateTimeOffset closedAt) =>
        OccupancyChangeSet.Restore(
            set.Id, set.OrderId, set.TargetId, set.PoleFeatureId, set.PoleOwnerOrganizationId, [.. set.Changes],
            set.BaselineVersion, status, set.StagedBy, set.StagedAt, closedAt);
}
