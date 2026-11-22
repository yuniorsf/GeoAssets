using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// <see cref="ServiceOrderRules"/>: how a principal that reaches an order only through cross-organization grants requiring
/// a scoped role (<see cref="WorkflowOrgGrant.RequiresScopedRole"/>, XD01-326) is decided — by the roles it holds on the
/// order's organization, never by its home roles.
/// </summary>
public sealed partial class ServiceOrderRules
{
    /// <summary>
    /// The reason given to a principal that reaches the order only through grants requiring a scoped role (XD01-326) and
    /// asks for an action none of them lets it take with the roles it holds on the order's organization.
    /// </summary>
    public const string NotGrantedOnOrganizationReason =
        "No grant lets you take this action with the roles you hold on the organization that owns the order.";

    /// <summary>
    /// The rule chain for a principal the organization gate let through. A principal that reaches the order only through
    /// grants requiring a scoped role (<see cref="OrderOrganizationGate.PassesOnlyThroughScopedRole"/>) is decided by what
    /// it holds on the order's organization: the action must be one such a grant lets it take (refused with
    /// <see cref="NotGrantedOnOrganizationReason"/> otherwise, whatever its home roles), and the chain then sees the
    /// principal with its home roles and permissions replaced by those held on that organization
    /// (<see cref="WorkflowPrincipal.OnOrganization"/>), so an order type's own requirements still hold. Every other
    /// principal: the chain, exactly as before.
    /// </summary>
    private RuleEvaluationResult EvaluateAdmitted(OrderAction action, RuleEvaluationContext context, DateTime utcNow)
    {
        if (!OrderOrganizationGate.PassesOnlyThroughScopedRole(context, utcNow))
            return EvaluateChain(action, context);

        if (!CrossOrgGrantMatcher.CoversAction(context.Principal, action, context.Order, utcNow))
            return RuleEvaluationResult.Deny(action, NotGrantedOnOrganizationReason, RuleDenialCode.NoRuleGranted);

        return EvaluateChain(action, context with { Principal = context.Principal.OnOrganization(context.Order.OrganizationId) });
    }
}
