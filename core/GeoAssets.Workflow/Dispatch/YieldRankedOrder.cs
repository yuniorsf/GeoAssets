namespace GeoAssets.Workflow.Dispatch;

/// <summary>
/// A <see cref="DispatchableOrder"/> augmented with the predicted US-value XD01-256's yield
/// optimizer ranks by. Urgency itself is derived from <see cref="DispatchableOrder.TimeWindowEnd"/>
/// (the order's SLA deadline — see <c>GeoAssets.Workflow.SlaGuard.SlaGuardEngine.DeadlineAttributeKey</c>)
/// at ranking time rather than stored here, so there is exactly one place ("is this order
/// urgent?") that decision gets made — see <c>GeoAssets.Workflow.Agents.Dispatch.OrderYieldRanker</c>.
/// </summary>
public sealed record YieldRankedOrder(DispatchableOrder Order, decimal PredictedValueUs);
