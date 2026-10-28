using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Stateless client contract for vertical domain microservices (e.g. <c>GEOS_FAT_IP</c>) to
/// query the Core GeoAssets Server for geographical features, alphanumeric attributes, and
/// topological relations (<see cref="Services.TopoGraph"/>) without loading the entire asset
/// catalog into local memory.
/// </summary>
public interface IGeoCoreClient
{
    /// <summary>Fetches a single feature by ID, or <c>null</c> if not found.</summary>
    Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Queries features matching type, organization, ID, and alphanumeric filters (<see cref="AssetQuery"/>).</summary>
    Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default);

    /// <summary>
    /// Returns the direct downstream neighbor feature IDs of <paramref name="featureId"/>
    /// filtered by optional <paramref name="edgeKind"/> (e.g. <c>"feeds"</c>) and
    /// <paramref name="targetAssetTypeId"/>.
    /// </summary>
    Task<IReadOnlyList<string>> GetNeighborIdsAsync(
        string featureId,
        string? edgeKind = null,
        string? targetAssetTypeId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Batch-resolves the direct downstream neighbor feature IDs for each feature ID in
    /// <paramref name="featureIds"/> in a single round-trip, filtered by optional
    /// <paramref name="edgeKind"/> and <paramref name="targetAssetTypeId"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(
        IReadOnlyList<string> featureIds,
        string? edgeKind = null,
        string? targetAssetTypeId = null,
        CancellationToken ct = default);
}

