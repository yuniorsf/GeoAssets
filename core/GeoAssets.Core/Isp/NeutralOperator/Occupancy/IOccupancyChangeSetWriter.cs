namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// Write side of the store of staged occupancy changes (XD01-315). The contract common to both
/// ports is on <see cref="IOccupancyChangeSetReader"/>. Nothing here ever touches a pole's live
/// occupancy.
/// </summary>
public interface IOccupancyChangeSetWriter
{
    /// <summary>
    /// Stores <paramref name="changeSet"/> as the staged set of its target, replacing the set
    /// staged there before, if any (the replaced set is gone; committed and discarded sets of
    /// the target are untouched). Safe to repeat. Returns the set as stored.
    ///
    /// <list type="number">
    ///   <item><description>Arguments, then cancellation.</description></item>
    ///   <item><description>The set must be <see cref="OccupancyChangeSetStatus.Staged"/> and
    ///   owned by the adapter's organization, else <see cref="ArgumentException"/>.</description></item>
    ///   <item><description>Its target must exist, belong to its order and to the same
    ///   organization, and be the target of the set's pole feature, else
    ///   <see cref="KeyNotFoundException"/>.</description></item>
    ///   <item><description>A target already released — its order has closed — accepts nothing:
    ///   <see cref="OccupancyTargetReleasedException"/>.</description></item>
    ///   <item><description>All or nothing: when the call throws, what was staged before is still
    ///   staged. Of several callers staging for one target at once, every call succeeds and
    ///   exactly one set remains staged, whole — never a mix of two.</description></item>
    /// </list>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="changeSet"/> is null.</exception>
    Task<OccupancyChangeSet> StageAsync(OccupancyChangeSet changeSet, CancellationToken ct);

    /// <summary>
    /// Discards the set staged on one target: it stays on file as
    /// <see cref="OccupancyChangeSetStatus.Discarded"/>, closed at the adapter's clock. Returns
    /// true when a staged set was discarded, false when the target had none (including an
    /// unknown order or target) — so repeating the call is harmless.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> or <paramref name="targetId"/> is null or blank.</exception>
    Task<bool> DiscardAsync(string orderId, string targetId, CancellationToken ct);

    /// <summary>
    /// Discards every set staged on the targets of one order, as <see cref="DiscardAsync"/> does
    /// for one, and returns how many were discarded (0 for an unknown order).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> is null or blank.</exception>
    Task<int> DiscardAllAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Records that a staged set was applied to its pole (XD01-318): the set on file with
    /// <paramref name="committed"/>'s id becomes <see cref="OccupancyChangeSetStatus.Committed"/>,
    /// closed at <paramref name="committed"/>'s <see cref="OccupancyChangeSet.ClosedAt"/>. Returns
    /// true when it did; false when no set with that id is still staged on file (it was replaced,
    /// discarded or already committed meanwhile) — then nothing changed. Joins the caller's
    /// transaction when there is one, so the status and the occupancy written from the set are
    /// stored together.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="committed"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="committed"/> is not <see cref="OccupancyChangeSetStatus.Committed"/>, or
    /// belongs to another organization than the adapter's.
    /// </exception>
    Task<bool> MarkCommittedAsync(OccupancyChangeSet committed, CancellationToken ct);
}
