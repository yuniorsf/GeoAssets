namespace GeoAssets.Core.Models;

/// <summary>
/// Request payload for batch topology neighbor resolution
/// (<c>POST /api/geoassets/features/topology/neighbors-batch</c>).
/// </summary>
public sealed record TopologyNeighborsBatchRequest
{
    public IReadOnlyList<string> FeatureIds { get; init; } = [];

    /// <summary>Optional <see cref="TopoEdge.Kind"/> filter (e.g. <c>"feeds"</c>).</summary>
    public string? Kind { get; init; }

    /// <summary>Optional target <see cref="GeoFeatureProperties.AssetTypeId"/> filter.</summary>
    public string? TargetAssetTypeId { get; init; }
}

