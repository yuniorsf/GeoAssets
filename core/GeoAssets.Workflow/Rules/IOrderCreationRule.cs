using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// A single evaluable rule in the service-order creation authorization chain.
/// Parallel to <see cref="IServiceOrderRule"/>, but evaluated against an
/// <see cref="OrderType"/> rather than an existing <see cref="Orders.IServiceOrder"/>,
/// since no order exists yet at creation time.
///
/// Return values follow the same three-value logic as <see cref="IServiceOrderRule"/>:
/// <c>true</c> (allow), <c>false</c> (deny, overrides any allow), <c>null</c> (abstain).
/// </summary>
public interface IOrderCreationRule
{
    /// <summary>Optional name for diagnostics.</summary>
    string Name { get; }

    bool? Evaluate(WorkflowPrincipal principal, OrderType orderType);
}
