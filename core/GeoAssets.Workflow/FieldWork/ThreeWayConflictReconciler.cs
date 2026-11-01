using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Deterministic 3-way offline conflict reconciliation (XD01-223): given the common
/// <paramref name="baseOrder"/> a device last synced before going offline, the device's own
/// <paramref name="localOrder"/> offline edits, and the server's current
/// <paramref name="remoteOrder"/> (which may have changed independently while the device was
/// offline), decides what the reconciled order should look like — consumed by the mobile sync
/// gateway (XD01-236), lives here as reusable pure domain logic (no infrastructure dependency).
///
/// <see cref="Reconcile"/> mutates and returns <paramref name="remoteOrder"/> in place — the same
/// "mutate and return this" convention <see cref="ServiceOrder.Transition(string, TimeProvider)"/>/
/// <see cref="ServiceOrder.RecordAction"/> already use — rather than cloning. Callers that need to
/// keep an unmodified copy of the server row for audit should snapshot it themselves before
/// calling.
/// </summary>
public static class ThreeWayConflictReconciler
{
    /// <summary>
    /// Classifies the 3-way diff. A structural collision is scoped specifically to this ticket's
    /// named scenario — the device completed the work while the server independently diverged
    /// (reassigned, or moved status to anything other than also-Completed) — not a general "any
    /// field conflict anywhere in the graph" rule, which would be a much larger, speculative
    /// surface this ticket's AC doesn't actually ask for.
    /// </summary>
    public static ConflictClassification Classify(ServiceOrder baseOrder, ServiceOrder localOrder, ServiceOrder remoteOrder)
    {
        ArgumentNullException.ThrowIfNull(baseOrder);
        ArgumentNullException.ThrowIfNull(localOrder);
        ArgumentNullException.ThrowIfNull(remoteOrder);

        if (IsUnchanged(baseOrder, localOrder))
            return ConflictClassification.TrivialCatchUp;

        var localCompleted = string.Equals(localOrder.Status, FieldWorkStatus.Completed, StringComparison.Ordinal);
        var remoteAlsoCompleted = string.Equals(remoteOrder.Status, FieldWorkStatus.Completed, StringComparison.Ordinal);
        var remoteStatusDiverged = !string.Equals(remoteOrder.Status, baseOrder.Status, StringComparison.Ordinal);
        var remoteAssignmentDiverged = !string.Equals(remoteOrder.AssignedTo, baseOrder.AssignedTo, StringComparison.Ordinal);

        var isStructuralCollision = localCompleted && !remoteAlsoCompleted && (remoteStatusDiverged || remoteAssignmentDiverged);

        return isStructuralCollision ? ConflictClassification.StructuralCollision : ConflictClassification.BenignDivergence;
    }

    /// <summary>Applies <see cref="Classify"/>'s decision, returning the reconciled order.</summary>
    public static ServiceOrder Reconcile(ServiceOrder baseOrder, ServiceOrder localOrder, ServiceOrder remoteOrder, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return Classify(baseOrder, localOrder, remoteOrder) switch
        {
            ConflictClassification.TrivialCatchUp => remoteOrder,
            ConflictClassification.StructuralCollision => EscalateToConflictReview(remoteOrder, timeProvider),
            _ => Merge(baseOrder, localOrder, remoteOrder, timeProvider),
        };
    }

    /// <summary>
    /// Moves a human reviewer's decision on a <see cref="FieldWorkStatus.ConflictReview"/> order
    /// to its outcome — <paramref name="approve"/> confirms the disputed work actually happened
    /// (→ <see cref="FieldWorkStatus.Inspected"/>); rejecting it bounces the order back to
    /// <see cref="FieldWorkStatus.Pending"/> for rescheduling. Unlike <see cref="Reconcile"/>'s
    /// own escalation path, adjudication is an ordinary, graph-legal action — goes through
    /// <see cref="ServiceOrder.Transition(string, TimeProvider, OrderType?)"/> like any other
    /// transition, so an invalid call (e.g. the order isn't actually in ConflictReview) still
    /// throws <see cref="InvalidServiceOrderTransitionException"/> the normal way.
    /// </summary>
    public static ServiceOrder Adjudicate(ServiceOrder order, bool approve, TimeProvider timeProvider, OrderType fieldWorkOrderType)
    {
        ArgumentNullException.ThrowIfNull(order);

        var newStatus = approve ? FieldWorkStatus.Inspected : FieldWorkStatus.Pending;
        return order.Transition(newStatus, timeProvider, fieldWorkOrderType);
    }

    /// <summary>
    /// Sets <see cref="ServiceOrder.Status"/> to <see cref="FieldWorkStatus.ConflictReview"/>
    /// directly, bypassing <see cref="ServiceOrder.Transition(string, TimeProvider, OrderType?)"/>'s
    /// normal graph-edge check. Deliberate: a structural collision can be detected while
    /// <paramref name="remoteOrder"/> sits in any state, including a graph-terminal one
    /// (<see cref="FieldWorkStatus.Cancelled"/> has no outgoing edges at all) — escalating an
    /// otherwise-irreconcilable state to human review is exactly what this engine exists to do,
    /// so it is the one place in this domain model allowed to move <see cref="ServiceOrder.Status"/>
    /// outside the normal graph. An ordinary caller must still go through
    /// <see cref="FieldWorkOrderTransitioner"/>/<see cref="ServiceOrder.Transition(string, TimeProvider, OrderType?)"/>.
    /// </summary>
    private static ServiceOrder EscalateToConflictReview(ServiceOrder remoteOrder, TimeProvider timeProvider)
    {
        remoteOrder.Status = FieldWorkStatus.ConflictReview;
        remoteOrder.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        return remoteOrder;
    }

    private static bool IsUnchanged(ServiceOrder baseOrder, ServiceOrder localOrder) =>
        string.Equals(baseOrder.Status, localOrder.Status, StringComparison.Ordinal)
        && string.Equals(baseOrder.AssignedTo, localOrder.AssignedTo, StringComparison.Ordinal)
        && AttributesEqual(baseOrder.Attributes, localOrder.Attributes);

    private static bool AttributesEqual(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Benign-divergence merge, mutating and returning <paramref name="remoteOrder"/>:
    /// attributes merge per-key (an offline-only local edit — changed from base, remote didn't
    /// touch that key — is preserved; everything else, including a genuine same-key double-edit,
    /// defers to remote — per-key attribute collisions are out of this ticket's "structural
    /// collision" scope, which is specifically about status/assignment), and the action log is
    /// the union of both sides' entries, deduplicated (<see cref="OrderActionLog"/> is a value
    /// record) and ordered chronologically.
    /// </summary>
    private static ServiceOrder Merge(ServiceOrder baseOrder, ServiceOrder localOrder, ServiceOrder remoteOrder, TimeProvider timeProvider)
    {
        var mergedAttributes = new Dictionary<string, string>(remoteOrder.Attributes, StringComparer.Ordinal);
        foreach (var (key, localValue) in localOrder.Attributes)
        {
            var baseHasKey = baseOrder.Attributes.TryGetValue(key, out var baseValue);
            var localChangedThisKey = !baseHasKey || !string.Equals(baseValue, localValue, StringComparison.Ordinal);
            if (!localChangedThisKey) continue;

            var remoteHasKey = remoteOrder.Attributes.TryGetValue(key, out var remoteValue);
            var remoteChangedThisKey = remoteHasKey != baseHasKey || (remoteHasKey && !string.Equals(baseValue, remoteValue, StringComparison.Ordinal));
            if (!remoteChangedThisKey)
                mergedAttributes[key] = localValue;
        }

        remoteOrder.Attributes.Clear();
        foreach (var (key, value) in mergedAttributes)
            remoteOrder.Attributes[key] = value;

        var mergedLog = localOrder.ActionLog
            .Concat(remoteOrder.ActionLog)
            .Distinct()
            .OrderBy(entry => entry.PerformedAt)
            .ToList();
        remoteOrder.ActionLog.Clear();
        remoteOrder.ActionLog.AddRange(mergedLog);

        remoteOrder.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        return remoteOrder;
    }
}
