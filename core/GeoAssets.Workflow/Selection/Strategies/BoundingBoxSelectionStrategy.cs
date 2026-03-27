using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Selects all features whose geometry falls within a bounding box drawn on the map.
///
/// Required parameters:
///   minLon  (double) — west boundary
///   minLat  (double) — south boundary
///   maxLon  (double) — east boundary
///   maxLat  (double) — north boundary
/// </summary>
[ExportFeatureSelectionStrategy("bounding-box",
    Category    = "Spatial",
    DisplayName = "Bounding Box",
    Description = "Selects features inside a rectangle drawn on the map.")]
public sealed class BoundingBoxSelectionStrategy : IFeatureSelectionStrategy
{
    public string StrategyId   => "bounding-box";
    public string DisplayName  => "Bounding Box";
    public string Description  => "Selects features inside a rectangle drawn on the map.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        var p = context.Parameters;

        var minLon = Convert.ToDouble(p["minLon"]);
        var minLat = Convert.ToDouble(p["minLat"]);
        var maxLon = Convert.ToDouble(p["maxLon"]);
        var maxLat = Convert.ToDouble(p["maxLat"]);

        // Build a polygon from the bounding box corners
        var bbox = new GeoPolygon
        {
            Coordinates =
            [
                [
                    [minLon, minLat], [maxLon, minLat],
                    [maxLon, maxLat], [minLon, maxLat],
                    [minLon, minLat]
                ]
            ]
        };

        var result = context.Repository.GetWithin(bbox);
        return Task.FromResult(result);
    }
}
