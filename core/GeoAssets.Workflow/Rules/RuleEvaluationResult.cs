using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// The result of evaluating whether a principal may perform an action on an order.
/// </summary>
public sealed record RuleEvaluationResult(
    bool               Allowed,
    OrderActionType    Action,
    string             Reason
)
{
    public static RuleEvaluationResult Allow(OrderActionType action, string reason)
        => new(true,  action, reason);

    public static RuleEvaluationResult Deny(OrderActionType action, string reason)
        => new(false, action, reason);
}
