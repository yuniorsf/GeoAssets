namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Write surface over the <see cref="ServiceOrderTarget"/>s of service orders; see
/// <see cref="IServiceOrderTargetReader"/> for the read side.
///
/// The member documentation below is the contract every adapter must honour identically.
/// Rules common to all members: a null argument throws <see cref="ArgumentNullException"/>;
/// a blank order id throws <see cref="ArgumentException"/>; no member ever changes a target of
/// an order other than the one named; a cancelled <see cref="CancellationToken"/> surfaces as
/// <see cref="OperationCanceledException"/>. Authorization is the caller's job, not the adapter's.
/// </summary>
public interface IServiceOrderTargetWriter
{
    /// <summary>
    /// Adds <paramref name="targets"/> to <paramref name="orderId"/> and returns how many were
    /// actually inserted. Idempotent on (order id, feature id):
    /// <list type="bullet">
    /// <item><description>
    /// A target whose feature the order already holds is skipped — never an error, never an
    /// update of the existing target. So is a second target for the same feature within
    /// <paramref name="targets"/> (the first one wins), and so is a target that loses a race
    /// with a concurrent add of the same feature. Repeating a call therefore returns 0.
    /// </description></item>
    /// <item><description>
    /// Empty <paramref name="targets"/> returns 0 without touching the store.
    /// </description></item>
    /// <item><description>
    /// A target whose <see cref="ServiceOrderTarget.OrderId"/> differs from
    /// <paramref name="orderId"/> throws <see cref="ArgumentException"/>, and nothing from the
    /// call is inserted.
    /// </description></item>
    /// <item><description>
    /// Inserted targets are stored as given, including <see cref="ServiceOrderTarget.Id"/> and
    /// <see cref="ServiceOrderTarget.Version"/> (0 for a new target).
    /// </description></item>
    /// </list>
    /// </summary>
    Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct);

    /// <summary>
    /// Persists the per-target work state of <paramref name="target"/>, located by its
    /// <see cref="ServiceOrderTarget.Id"/> within its <see cref="ServiceOrderTarget.OrderId"/>.
    /// <list type="bullet">
    /// <item><description>
    /// Written: <see cref="ServiceOrderTarget.ActivityIds"/>, <see cref="ServiceOrderTarget.Status"/>,
    /// <see cref="ServiceOrderTarget.ConflictLevel"/> and <see cref="ServiceOrderTarget.Note"/>.
    /// Every other member is fixed when the target is added and is ignored here.
    /// </description></item>
    /// <item><description>
    /// No such target in that order throws <see cref="KeyNotFoundException"/>.
    /// </description></item>
    /// <item><description>
    /// Optimistic concurrency: if <see cref="ServiceOrderTarget.Version"/> is not the stored
    /// version, throws <see cref="Orders.ServiceOrderConcurrencyException"/> and writes
    /// nothing. On success the stored version becomes <c>target.Version + 1</c>, so a caller
    /// that keeps working with the same target continues from that value (or re-reads it).
    /// </description></item>
    /// <item><description>
    /// The adapter does not re-check status moves or the activity catalog; callers go through
    /// <see cref="ServiceOrderTarget.WithStatus"/> and <see cref="ServiceOrderTarget.WithActivities"/>.
    /// </description></item>
    /// </list>
    /// </summary>
    Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct);

    /// <summary>
    /// Removes the targets of <paramref name="orderId"/> whose <see cref="ServiceOrderTarget.Id"/>
    /// is in <paramref name="targetIds"/> and returns how many were removed. Ids that match no
    /// target of that order — unknown, already removed, or belonging to another order — are
    /// ignored, so repeating a call returns 0. Empty <paramref name="targetIds"/> returns 0
    /// without touching the store. No version check applies.
    /// </summary>
    Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct);
}
