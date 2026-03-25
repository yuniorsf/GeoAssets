using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Selects features by an explicit list of IDs provided by the user
/// (e.g. picking assets one by one on the map).
///
/// Required parameters:
///   featureIds  (IEnumerable&lt;string&gt;) — IDs of the features to include
/// </summary>
[ExportFeatureSelectionStrategy("manual",
    Category    = "Interactive",
    DisplayName = "Manual Selection",
    Description = "User picks features one by one on the map.")]
public sealed class ManualSelectionStrategy : IFeatureSelectionStrategy
{
    public string StrategyId  => "manual";
    public string DisplayName => "Manual Selection";
    public string Description => "User picks features one by one on the map.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        var ids = (IEnumerable<string>)context.Parameters["featureIds"];

        var result = ids
            .Select(id => context.Repository.GetById(id))
            .Where(f => f is not null)
            .Cast<GeoFeature>()
            .ToList();

        return Task.FromResult<IReadOnlyList<GeoFeature>>(result);
    }
}
