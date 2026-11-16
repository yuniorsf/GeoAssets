using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Selects the features covered by a polygon fence drawn on the map. The boundary is inclusive:
/// a feature lying exactly on the fence's edge is selected (where <c>bounding-box</c> uses a strict
/// "within"). The fence is validated by <see cref="SelectionFenceValidator"/> and rejected, never
/// repaired, when it is not a valid closed polygon.
///
/// Required parameters:
///   rings  (double[][][]) — RFC 7946 polygon coordinates: exterior ring first, then holes; each
///                           position is [longitude, latitude] and each ring is closed
///
/// Optional parameters:
///   assetTypeIds  (IEnumerable&lt;string&gt;) — keep only these asset types; absent or empty keeps all
///   maxResults    (int, default unbounded)  — cut the result, ordered by feature id, at this size
///
/// Results are ordered by feature id (ordinal, ascending), so a truncated result is deterministic.
/// </summary>
[ExportFeatureSelectionStrategy(Id,
    Category    = "Spatial",
    DisplayName = "Polygon Fence",
    Description = "Selects features covered by a polygon drawn on the map.")]
public sealed class PolygonFenceSelectionStrategy : IFeatureSelectionStrategy
{
    public const string Id                    = "polygon-fence";
    public const string RingsParameter        = "rings";
    public const string AssetTypeIdsParameter = "assetTypeIds";
    public const string MaxResultsParameter   = "maxResults";

    public string StrategyId  => Id;
    public string DisplayName => "Polygon Fence";
    public string Description => "Selects features covered by a polygon drawn on the map.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var p = context.Parameters;

        var area         = SelectionFenceValidator.Validate(p.GetRings(RingsParameter));
        var assetTypeIds = p.GetOptionalStringList(AssetTypeIdsParameter);
        var maxResults   = p.GetOptionalInt32(MaxResultsParameter) ?? int.MaxValue;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);

        var filter = new AssetSpatialFilter(
            assetTypeIds is { Count: > 0 } ? assetTypeIds : null,
            context.OrganizationId,
            maxResults);

        return context.SpatialQuery.FindCoveredByAsync(area, filter, ct);
    }
}
