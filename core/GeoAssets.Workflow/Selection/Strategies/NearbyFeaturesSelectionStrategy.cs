using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Selects features within a radius of a center point.
/// Delegates to the repository's NTS-backed spatial query.
///
/// Required parameters:
///   center         (GeoPoint) — origin of the search
///   radiusDegrees  (double)   — search radius in degrees
/// </summary>
[ExportFeatureSelectionStrategy("nearby",
    Category    = "Spatial",
    DisplayName = "Nearby Features",
    Description = "Selects features within a radius of a center point.")]
public sealed class NearbyFeaturesSelectionStrategy : IFeatureSelectionStrategy
{
    public string StrategyId  => "nearby";
    public string DisplayName => "Nearby Features";
    public string Description => "Selects features within a radius of a center point.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        var center    = (GeoPoint)context.Parameters["center"];
        var radiusDeg = Convert.ToDouble(context.Parameters["radiusDegrees"]);

        var result = context.Repository.GetNearby(center, radiusDeg);
        return Task.FromResult(result);
    }
}
