using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Inherits the feature set from the target order's parent order.
/// Useful when a child work order operates on the same assets as its parent
/// but applies a different kind of intervention.
///
/// Required context: TargetOrder with a non-null ParentOrderId, and OrderRepository.
///
/// Optional parameters:
///   filter  (Func&lt;GeoFeature, bool&gt;) — narrows the inherited set.
///           A delegate cannot be JSON-serialized, so <see cref="FeatureSelectionRegistry.SelectAsync"/>
///           rejects it: only use <c>filter</c> when calling this strategy directly and
///           discarding the resulting <see cref="FeatureSelectionSpec"/> rather than persisting it.
/// </summary>
[ExportFeatureSelectionStrategy("inherit-parent",
    Category    = "Hierarchy",
    DisplayName = "Inherit from Parent Order",
    Description = "Copies the feature set from the parent service order.")]
public sealed class InheritFromParentOrderStrategy : IFeatureSelectionStrategy
{
    public string StrategyId  => "inherit-parent";
    public string DisplayName => "Inherit from Parent Order";
    public string Description => "Copies the feature set from the parent service order.";

    public async Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        if (context.TargetOrder is null)
            throw new InvalidOperationException("TargetOrder must be set for 'inherit-parent' strategy.");

        if (context.OrderRepository is null)
            throw new InvalidOperationException("OrderRepository must be set for 'inherit-parent' strategy.");

        if (context.TargetOrder.ParentOrderId is null)
            return [];

        var parent = await context.OrderRepository.GetByIdAsync(context.TargetOrder.ParentOrderId, ct);
        if (parent is null)
            return [];

        IEnumerable<GeoFeature> features = parent.Features;

        if (context.Parameters.TryGetValue("filter", out var raw) && raw is Func<GeoFeature, bool> predicate)
            features = features.Where(predicate);

        return [.. features];
    }
}
