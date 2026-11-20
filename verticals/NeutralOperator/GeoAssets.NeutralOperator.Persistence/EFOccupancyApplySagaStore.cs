using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IOccupancyApplySagaStore"/> and <see cref="IOccupancyApplySagaMonitor"/> over
/// <c>occupancy_apply_sagas</c>, <c>occupancy_apply_saga_poles</c> and the change sets' <c>ApplyBatchId</c> (XD01-383). The
/// contract is the member documentation on those ports; what follows is only how.
///
/// <para><b>Transactions.</b> Every write joins the ambient (kernel unit-of-work) transaction first, like the other
/// occupancy stores, so a saga changes with the order, the change sets, the poles and the outbox message it belongs to.
/// State moves are compare-and-set <c>UPDATE … WHERE State = 'Pending'</c> statements past the change tracker: of two
/// replies racing for one saga, one matches and the other changes nothing.</para>
///
/// <para><b>Tenancy.</b> Owner only on every port member but the monitor's, which reads and flags the headers across
/// organizations (<c>IgnoreQueryFilters</c>) and never touches a pole snapshot.</para>
/// </summary>
public sealed class EFOccupancyApplySagaStore : IOccupancyApplySagaStore, IOccupancyApplySagaMonitor
{
    private static readonly string PendingState = OccupancyApplySagaMapper.ToStateText(OccupancyApplySagaState.Pending);
    private static readonly string StagedStatus = OccupancyChangeSetMapper.ToStatusText(OccupancyChangeSetStatus.Staged);
    private static readonly string PendingStatus = OccupancyChangeSetMapper.ToStatusText(OccupancyChangeSetStatus.Pending);

    private readonly NeutralOperatorDbContext _db;
    private readonly EFPoleOccupancyRepository _poles;

    /// <param name="db">The scoped context, on the connection shared with the kernel's unit of work.</param>
    /// <param name="poles">The pole store on the same context, which restores a reverted saga's poles.</param>
    public EFOccupancyApplySagaStore(NeutralOperatorDbContext db, EFPoleOccupancyRepository poles)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(poles);

        _db = db;
        _poles = poles;
    }

    public async Task<bool> BeginAsync(
        OccupancyApplySaga saga, IReadOnlyCollection<OccupancyApplySagaPole> poles, IReadOnlyCollection<Guid> changeSetIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(poles);
        ArgumentNullException.ThrowIfNull(changeSetIds);
        if (saga.State != OccupancyApplySagaState.Pending)
            throw new ArgumentException($"A saga begins pending; this one is {saga.State}.", nameof(saga));
        if (saga.OrganizationId != _db.CurrentOrganizationId)
            throw new ArgumentException("The saga belongs to another organization than the store's.", nameof(saga));
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var ids = changeSetIds.Distinct().ToArray();
        var (batchId, stagedStatus, pendingStatus) = (saga.BatchId, StagedStatus, PendingStatus);
        DateTime? writtenAt = OccupancyChangeSetMapper.WholeMicroseconds(saga.RequestedAt);
        var moved = await _db.OccupancyChangeSets
            .Where(s => ids.Contains(s.Id) && s.Status == stagedStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, pendingStatus)
                    .SetProperty(s => s.ClosedAt, writtenAt)
                    .SetProperty(s => s.ApplyBatchId, (Guid?)batchId),
                ct).ConfigureAwait(false);
        if (moved != ids.Length)
            return false;

        _db.OccupancyApplySagas.Add(OccupancyApplySagaMapper.ToRecord(saga));
        _db.OccupancyApplySagaPoles.AddRange(poles.Select(pole => OccupancyApplySagaMapper.ToRecord(saga.BatchId, saga.OrganizationId, pole)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _db.ChangeTracker.Clear();
        return true;
    }

    public async Task<OccupancyApplySaga?> FindAsync(Guid batchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var record = await Sagas.FirstOrDefaultAsync(s => s.BatchId == batchId, ct).ConfigureAwait(false);
        return record is null ? null : OccupancyApplySagaMapper.ToDomain(record);
    }

    public async Task<OccupancyApplySaga?> FindLatestForOrderAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var record = await Sagas
            .Where(s => s.ServiceOrderId == orderId)
            .OrderByDescending(s => s.Attempt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return record is null ? null : OccupancyApplySagaMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<string>> FindPendingPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureIds);
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var ids = poleFeatureIds.Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return [];

        var pendingState = PendingState;
        var held = await (
                from pole in _db.OccupancyApplySagaPoles.AsNoTracking()
                join saga in Sagas on pole.BatchId equals saga.BatchId
                where saga.State == pendingState && ids.Contains(pole.PoleFeatureId)
                select pole.PoleFeatureId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        held.Sort(StringComparer.Ordinal);
        return held;
    }

    public async Task<IReadOnlyList<OccupancyApplySagaPole>> GetPolesAsync(Guid batchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var records = await _db.OccupancyApplySagaPoles.AsNoTracking()
            .Where(p => p.BatchId == batchId)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. records.Select(OccupancyApplySagaMapper.ToDomain).OrderBy(p => p.PoleFeatureId, StringComparer.Ordinal)];
    }

    public async Task<bool> CompleteAsync(OccupancyApplySaga completed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (completed.State == OccupancyApplySagaState.Pending || completed.CompletedAt is null)
            throw new ArgumentException("A saga completes as committed or reverted, at an instant.", nameof(completed));
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var (batchId, pendingState) = (completed.BatchId, PendingState);
        var state = OccupancyApplySagaMapper.ToStateText(completed.State);
        DateTime? completedAt = OccupancyChangeSetMapper.WholeMicroseconds(completed.CompletedAt.Value);
        var (reasonCode, reason) = (completed.ReasonCode, completed.Reason);
        var conflicting = OccupancyApplySagaMapper.ToJson(completed.ConflictingFeatureIds);
        var updated = await Sagas
            .Where(s => s.BatchId == batchId && s.State == pendingState)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.State, state)
                    .SetProperty(s => s.CompletedAt, completedAt)
                    .SetProperty(s => s.ReasonCode, reasonCode)
                    .SetProperty(s => s.Reason, reason)
                    .SetProperty(s => s.ConflictingFeatureIdsJson, conflicting),
                ct).ConfigureAwait(false);
        if (updated == 0)
            return false;

        if (completed.State == OccupancyApplySagaState.Reverted)
        {
            foreach (var pole in await GetPolesAsync(batchId, ct).ConfigureAwait(false))
                await _poles.RestoreEntriesAsync(pole.PoleFeatureId, pole.AppliedVersion, pole.BaselineEntries, ct).ConfigureAwait(false);
        }

        var pendingStatus = PendingStatus;
        var settledStatus = OccupancyChangeSetMapper.ToStatusText(completed.State == OccupancyApplySagaState.Committed
            ? OccupancyChangeSetStatus.Committed
            : OccupancyChangeSetStatus.Reverted);
        await _db.OccupancyChangeSets
            .Where(s => s.ApplyBatchId == batchId && s.Status == pendingStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, settledStatus)
                    .SetProperty(s => s.ClosedAt, completedAt),
                ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RecordRetryAsync(Guid batchId, DateTimeOffset retriedAt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.JoinAmbientTransaction();

        var pendingState = PendingState;
        var at = OccupancyChangeSetMapper.WholeMicroseconds(retriedAt);
        var updated = await Sagas
            .Where(s => s.BatchId == batchId && s.State == pendingState)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.RetryCount, s => s.RetryCount + 1)
                    .SetProperty(s => s.LastRequestedAt, at)
                    .SetProperty(s => s.TimedOutAt, (DateTime?)null),
                ct).ConfigureAwait(false);
        return updated == 1;
    }

    // ── Monitor (every organization) ──────────────────────────────────────────

    public async Task<IReadOnlyList<OccupancyApplySaga>> MarkTimedOutAsync(DateTimeOffset staleBefore, DateTimeOffset now, int max, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);
        ct.ThrowIfCancellationRequested();

        var (pendingState, cutoff) = (PendingState, OccupancyChangeSetMapper.WholeMicroseconds(staleBefore));
        var stale = await AllSagas
            .Where(s => s.State == pendingState && s.TimedOutAt == null && s.LastRequestedAt < cutoff)
            .OrderBy(s => s.LastRequestedAt)
            .ThenBy(s => s.BatchId)
            .Take(max)
            .ToListAsync(ct).ConfigureAwait(false);
        if (stale.Count == 0) return [];

        var ids = stale.Select(s => s.BatchId).ToArray();
        DateTime? flaggedAt = OccupancyChangeSetMapper.WholeMicroseconds(now);
        var flagged = await _db.OccupancyApplySagas.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.BatchId) && s.State == pendingState && s.TimedOutAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.TimedOutAt, flaggedAt), ct).ConfigureAwait(false);

        return flagged == 0
            ? []
            : [.. stale.Select(record => OccupancyApplySagaMapper.ToDomain(record) with { TimedOutAt = now })];
    }

    public async Task<(int Pending, DateTimeOffset? OldestLastRequestedAt)> GetBacklogAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var pendingState = PendingState;
        var pending = AllSagas.Where(s => s.State == pendingState);
        var count = await pending.CountAsync(ct).ConfigureAwait(false);
        if (count == 0) return (0, null);

        var oldest = await pending.MinAsync(s => s.LastRequestedAt, ct).ConfigureAwait(false);
        return (count, new DateTimeOffset(StoredInstant.ToUtc(oldest)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<OccupancyApplySagaRecord> Sagas => _db.OccupancyApplySagas.AsNoTracking();

    private IQueryable<OccupancyApplySagaRecord> AllSagas => _db.OccupancyApplySagas.IgnoreQueryFilters().AsNoTracking();
}
