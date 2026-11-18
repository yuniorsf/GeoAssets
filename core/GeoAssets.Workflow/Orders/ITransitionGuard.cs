namespace GeoAssets.Workflow.Orders;

/// <summary>
/// A precondition on an order's status transition beyond the workflow graph (XD01-362), evaluated by
/// <see cref="ValidatingServiceOrderRepository"/> before the transition is persisted. A denial blocks it with a
/// <see cref="TransitionGuardDeniedException"/>.
/// </summary>
public interface ITransitionGuard
{
    Task<GuardVerdict> EvaluateAsync(OrderTransitionContext context, CancellationToken cancellationToken);
}
