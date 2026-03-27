using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// A single evaluable rule in the service order authorization chain.
///
/// Return values follow three-value logic:
///   • <c>true</c>  — explicitly ALLOW this action
///   • <c>false</c> — explicitly DENY this action (overrides any allow)
///   • <c>null</c>  — abstain; let other rules decide
///
/// The composite evaluator in <see cref="ServiceOrderRules"/> uses deny-overrides:
/// any <c>false</c> result wins over all <c>true</c> results.
/// </summary>
public interface IServiceOrderRule
{
    /// <summary>Optional name for diagnostics.</summary>
    string Name { get; }

    bool? Evaluate(OrderActionType action, RuleEvaluationContext context);
}
