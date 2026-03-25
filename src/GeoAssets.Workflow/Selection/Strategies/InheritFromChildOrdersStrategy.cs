using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Aggregates all features from the target order's direct child orders.
/// Useful for roll-up orders that summarise the scope of their sub-tasks.
///
/// Required context: TargetOrder and OrderRepository.
/// </summary>
[ExportFeatureSelectionStrategy("inherit-children",
    Category    = "Hierarchy",
    DisplayName = "Aggregate from Child Orders",
    Description = "Merges the feature sets of all direct child orders.")]
public sealed class InheritFromChildOrdersStrategy : IFeatureSelectionStrategy
{
    public string StrategyId  => "inherit-children";
    public string DisplayName => "Aggregate from Child Orders";
    public string Description => "Merges the feature sets of all direct child orders.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        if (context.TargetOrder is null)
            throw new InvalidOperationException("TargetOrder must be set for 'inherit-children' strategy.");

        if (context.OrderRepository is null)
            throw new InvalidOperationException("OrderRepository must be set for 'inherit-children' strategy.");

        var seen     = new HashSet<string>();
        var features = new List<GeoFeature>();

        foreach (var childId in context.TargetOrder.ChildOrderIds)
        {
            var child = context.OrderRepository.GetById(childId);
            if (child is null) continue;

            foreach (var f in child.Features)
            {
                if (seen.Add(f.Id))
                    features.Add(f);
            }
        }

        return Task.FromResult<IReadOnlyList<GeoFeature>>(features);
    }
}
