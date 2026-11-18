namespace GeoAssets.Workflow.Orders;

/// <summary>
/// A module's side effect of an order's status transition (XD01-362, ADR-001 smell J), run by the persistence adapter after
/// the transition is validated and before it is saved, in the same transaction: a hook that throws rolls the transition back.
/// Called for every status change; check <see cref="OrderTransitionContext.IsTerminal"/> for closing-only effects.
/// </summary>
public interface IOrderLifecycleHook
{
    Task OnTransitionedAsync(OrderTransitionContext context, CancellationToken cancellationToken);
}
