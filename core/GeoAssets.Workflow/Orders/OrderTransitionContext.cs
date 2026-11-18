namespace GeoAssets.Workflow.Orders;

/// <summary>
/// A status transition of an order (XD01-362): what <see cref="ITransitionGuard"/>s evaluate before it is persisted and what
/// <see cref="IOrderLifecycleHook"/>s react to inside the same unit of work. <see cref="OccurredAt"/> is the instant the
/// transition is recorded with, e.g. the release instant of the order's targets when it closes.
/// </summary>
public sealed record OrderTransitionContext(
    IServiceOrder Order, OrderType? OrderType, string FromStatus, string ToStatus, DateTimeOffset OccurredAt)
{
    /// <summary>Whether <see cref="ToStatus"/> is a terminal state of <see cref="OrderType"/>.</summary>
    public bool IsTerminal => ServiceOrderTransitions.IsTerminalState(OrderType, ToStatus);
}
