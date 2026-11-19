using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// The result of evaluating whether a principal may perform an action on an order.
/// </summary>
public sealed record RuleEvaluationResult(
    bool               Allowed,
    OrderAction    Action,
    string             Reason
)
{
    /// <summary>
    /// Why the action was refused; <see cref="RuleDenialCode.None"/> when it was allowed.
    /// </summary>
    public RuleDenialCode Code { get; init; }

    /// <summary>
    /// True when the principal failed the organization gate (XD01-332) — see
    /// <see cref="RuleDenialCode.OutsideOrganization"/> for what a host must do with it.
    /// </summary>
    public bool IsOutsideOrganization => Code == RuleDenialCode.OutsideOrganization;

    public static RuleEvaluationResult Allow(OrderAction action, string reason)
        => new(true,  action, reason);

    public static RuleEvaluationResult Deny(OrderAction action, string reason)
        => new(false, action, reason) { Code = RuleDenialCode.DeniedByRule };

    public static RuleEvaluationResult Deny(OrderAction action, string reason, RuleDenialCode code)
        => new(false, action, reason) { Code = code };
}
