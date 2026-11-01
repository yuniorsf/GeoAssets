using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Entry point for moving a <see cref="ServiceOrder"/> through
/// <see cref="FieldWorkWorkflowGraph"/>: validates the edge is structurally legal (via
/// <see cref="ServiceOrderTransitions.IsValid(OrderType?, string, string)"/>), additionally
/// consults <see cref="IFieldWorkTransitionGuard"/> for the two guarded edges, then applies the
/// mutation through <see cref="ServiceOrder.Transition(string, TimeProvider, OrderType?)"/>.
/// Pure domain logic — no infrastructure dependency.
/// </summary>
public static class FieldWorkOrderTransitioner
{
    /// <summary>
    /// Throws <see cref="InvalidServiceOrderTransitionException"/> both for a structurally
    /// illegal edge and for a structurally legal one whose guard rejects it — callers don't need
    /// to distinguish the two to handle the failure.
    /// </summary>
    public static ServiceOrder Transition(
        ServiceOrder order,
        string newStatus,
        TimeProvider timeProvider,
        OrderType fieldWorkOrderType,
        IFieldWorkTransitionGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(fieldWorkOrderType);
        guard ??= NullFieldWorkTransitionGuard.Instance;

        if (IsGuardedEdge(order.Status, newStatus, FieldWorkStatus.EnRoute, FieldWorkStatus.OnSite) && !guard.CanArrive(order))
            throw new InvalidServiceOrderTransitionException(order.Status, newStatus);

        if (IsGuardedEdge(order.Status, newStatus, FieldWorkStatus.OnSite, FieldWorkStatus.InProgress) && !guard.CanStartWork(order))
            throw new InvalidServiceOrderTransitionException(order.Status, newStatus);

        return order.Transition(newStatus, timeProvider, fieldWorkOrderType);
    }

    private static bool IsGuardedEdge(string from, string to, string guardedFrom, string guardedTo) =>
        string.Equals(from, guardedFrom, StringComparison.Ordinal) && string.Equals(to, guardedTo, StringComparison.Ordinal);
}
