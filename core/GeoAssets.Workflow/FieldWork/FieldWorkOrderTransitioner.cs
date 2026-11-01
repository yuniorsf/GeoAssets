using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Entry point for moving a <see cref="ServiceOrder"/> through
/// <see cref="FieldWorkWorkflowGraph"/>: validates the edge is structurally legal (via
/// <see cref="ServiceOrderTransitions.IsValid(OrderType?, string, string)"/>), additionally
/// consults <see cref="IFieldWorkTransitionGuard"/> for the two guarded edges, then applies the
/// mutation through <see cref="ServiceOrder.Transition(string, TimeProvider, OrderType?)"/>.
/// Pure domain logic — no infrastructure dependency of its own (a supplied
/// <see cref="IFieldWorkTransitionGuard"/> may have one).
/// </summary>
public static class FieldWorkOrderTransitioner
{
    /// <summary>
    /// Throws <see cref="InvalidServiceOrderTransitionException"/> both for a structurally
    /// illegal edge and for a structurally legal one whose guard rejects it — callers don't need
    /// to distinguish the two to handle the failure.
    ///
    /// <paramref name="devicePoint"/> is required (throws <see cref="ArgumentException"/> if
    /// null) only when the edge being attempted is <see cref="FieldWorkStatus.EnRoute"/> →
    /// <see cref="FieldWorkStatus.OnSite"/> — every other edge ignores it.
    /// </summary>
    public static async Task<ServiceOrder> TransitionAsync(
        ServiceOrder order,
        string newStatus,
        TimeProvider timeProvider,
        OrderType fieldWorkOrderType,
        IFieldWorkTransitionGuard? guard = null,
        GeoPoint? devicePoint = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(fieldWorkOrderType);
        guard ??= NullFieldWorkTransitionGuard.Instance;

        if (IsGuardedEdge(order.Status, newStatus, FieldWorkStatus.EnRoute, FieldWorkStatus.OnSite))
        {
            if (devicePoint is null)
                throw new ArgumentException($"A device point is required for the {FieldWorkStatus.EnRoute} -> {FieldWorkStatus.OnSite} transition.", nameof(devicePoint));

            if (!await guard.CanArriveAsync(order, devicePoint, ct).ConfigureAwait(false))
                throw new InvalidServiceOrderTransitionException(order.Status, newStatus);
        }

        if (IsGuardedEdge(order.Status, newStatus, FieldWorkStatus.OnSite, FieldWorkStatus.InProgress)
            && !await guard.CanStartWorkAsync(order, ct).ConfigureAwait(false))
            throw new InvalidServiceOrderTransitionException(order.Status, newStatus);

        if (IsGuardedEdge(order.Status, newStatus, FieldWorkStatus.InProgress, FieldWorkStatus.Completed)
            && !await guard.CanCompleteAsync(order, ct).ConfigureAwait(false))
            throw new InvalidServiceOrderTransitionException(order.Status, newStatus);

        return order.Transition(newStatus, timeProvider, fieldWorkOrderType);
    }

    private static bool IsGuardedEdge(string from, string to, string guardedFrom, string guardedTo) =>
        string.Equals(from, guardedFrom, StringComparison.Ordinal) && string.Equals(to, guardedTo, StringComparison.Ordinal);
}
