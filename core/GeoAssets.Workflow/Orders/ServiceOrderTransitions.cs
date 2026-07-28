namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Defines which <see cref="ServiceOrderStatus"/> transitions are structurally legal,
/// independent of who is performing them — see <see cref="Rules.ServiceOrderRules"/>
/// for the separate "who is allowed" gate.
///
/// Consulted by every write path that can change <see cref="IServiceOrder.Status"/>
/// (<c>InMemoryServiceOrderRepository</c> and <c>EFServiceOrderRepository</c>'s
/// <c>UpdateAsync</c>/<c>AppendActionAsync</c>, and <see cref="ServiceOrder.Transition"/>),
/// so the workflow graph has a single source of truth instead of being reimplemented
/// ad hoc wherever a status check happens to be needed.
/// </summary>
public static class ServiceOrderTransitions
{
    private static readonly Dictionary<ServiceOrderStatus, ServiceOrderStatus[]> _allowed = new()
    {
        [ServiceOrderStatus.Draft]      = [ServiceOrderStatus.Pending, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.Pending]    = [ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.InProgress] = [ServiceOrderStatus.OnHold, ServiceOrderStatus.Completed, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.OnHold]     = [ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.Completed]  = [],
        [ServiceOrderStatus.Cancelled]  = [],
    };

    /// <summary>
    /// Returns true if moving from <paramref name="from"/> to <paramref name="to"/> is
    /// structurally legal. Staying in the same status is always allowed (a no-op write).
    /// <see cref="ServiceOrderStatus.Completed"/> and <see cref="ServiceOrderStatus.Cancelled"/>
    /// are terminal — no transition out of either is legal.
    /// </summary>
    public static bool IsValid(ServiceOrderStatus from, ServiceOrderStatus to)
        => from == to || _allowed[from].Contains(to);
}

/// <summary>
/// Thrown when a caller attempts a <see cref="ServiceOrderStatus"/> transition that
/// <see cref="ServiceOrderTransitions.IsValid"/> rejects as structurally illegal
/// (e.g. reopening a <see cref="ServiceOrderStatus.Completed"/> order, or skipping
/// straight from <see cref="ServiceOrderStatus.Draft"/> to <see cref="ServiceOrderStatus.Completed"/>).
/// </summary>
public sealed class InvalidServiceOrderTransitionException(ServiceOrderStatus from, ServiceOrderStatus to)
    : InvalidOperationException($"Cannot transition a service order from '{from}' to '{to}'.")
{
    public ServiceOrderStatus From { get; } = from;
    public ServiceOrderStatus To   { get; } = to;
}
