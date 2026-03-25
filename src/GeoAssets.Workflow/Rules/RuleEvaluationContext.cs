using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// All data required by a rule to reach a decision.
/// Computed once per <see cref="ServiceOrderRules.Evaluate"/> call.
/// </summary>
public sealed record RuleEvaluationContext(
    WorkflowPrincipal    Principal,
    IServiceOrder        Order,
    OrderUserRelationship Relationship
)
{
    public bool Is(OrderUserRelationship flag) => (Relationship & flag) != 0;
}
