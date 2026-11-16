namespace GeoAssets.Workflow.Rules;

/// <summary>
/// Why <see cref="ServiceOrderRules.Evaluate"/> refused an action — a stable value a host can
/// switch on, where <see cref="RuleEvaluationResult.Reason"/> is text for people.
/// </summary>
public enum RuleDenialCode
{
    /// <summary>The action was allowed.</summary>
    None = 0,

    /// <summary>The principal has no user id.</summary>
    Anonymous = 1,

    /// <summary>
    /// The principal failed the organization gate (XD01-332): it does not belong to the order's
    /// organization, its organization holds no cross-organization grant covering the order, and
    /// the order was not dispatched to it. No other rule was consulted. A host should answer
    /// exactly as it does for an order that does not exist.
    /// </summary>
    OutsideOrganization = 2,

    /// <summary>The principal passed the organization gate and a rule explicitly denied the action.</summary>
    DeniedByRule = 3,

    /// <summary>The principal passed the organization gate and no rule granted the action.</summary>
    NoRuleGranted = 4,
}
