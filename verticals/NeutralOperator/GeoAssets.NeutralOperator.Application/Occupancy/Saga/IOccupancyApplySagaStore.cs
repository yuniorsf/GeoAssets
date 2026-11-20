namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// The store of occupancy approvals' apply sagas (XD01-383), scoped like the occupancy stores to the caller's organization
/// — the pole owner — and joining the caller's unit of work, so a saga, its poles and its change sets change together with
/// the order and the outbox message they belong to. Its reads are <see cref="IOccupancyApplySagaReader"/>'s.
/// </summary>
public interface IOccupancyApplySagaStore : IOccupancyApplySagaReader
{
    /// <summary>
    /// Records a new <see cref="OccupancyApplySagaState.Pending"/> saga with its <paramref name="poles"/>, and moves each of
    /// <paramref name="changeSetIds"/> from <c>Staged</c> to <c>Pending</c> under the saga's batch. Returns false — and the
    /// caller rolls back — when one of them is no longer staged (replaced, discarded or committed meanwhile).
    /// </summary>
    Task<bool> BeginAsync(
        OccupancyApplySaga saga, IReadOnlyCollection<OccupancyApplySagaPole> poles, IReadOnlyCollection<Guid> changeSetIds, CancellationToken ct);

    /// <summary>
    /// Ends a pending saga as <paramref name="completed"/> says (<see cref="OccupancyApplySagaState.Committed"/> or
    /// <see cref="OccupancyApplySagaState.Reverted"/>, with its instant and reason) and moves its change sets from
    /// <c>Pending</c> to <c>Committed</c> or <c>Reverted</c>. Reverting also restores every pole of the saga to its
    /// <see cref="OccupancyApplySagaPole.BaselineEntries"/> as its next version — a compensation, not a revision, so a slot
    /// status may move back — and throws <see cref="Domain.Occupancy.OccupancyConflictException"/> when a pole is no longer at
    /// the version the approval left it at. Returns false, changing nothing, when the saga is no longer pending.
    /// </summary>
    Task<bool> CompleteAsync(OccupancyApplySaga completed, CancellationToken ct);

    /// <summary>
    /// Records an operator's retry of a pending saga at <paramref name="retriedAt"/>: one more retry, the timeout restarted.
    /// Returns false when the saga is no longer pending.
    /// </summary>
    Task<bool> RecordRetryAsync(Guid batchId, DateTimeOffset retriedAt, CancellationToken ct);
}
