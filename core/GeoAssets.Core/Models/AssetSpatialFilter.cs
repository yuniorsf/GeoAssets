namespace GeoAssets.Core.Models;

/// <summary>
/// Narrowing and bounding criteria shared by every <see cref="Interfaces.IAssetSpatialQuery"/> lookup.
/// </summary>
/// <param name="AssetTypeIds">
/// Asset type ids to keep, matched against <see cref="GeoFeatureProperties.AssetTypeId"/> with ordinal
/// comparison. <c>null</c> or empty applies no type filter.
/// </param>
/// <param name="OrganizationId">
/// When set, only features whose <see cref="GeoFeatureProperties.OrganizationId"/> equals it are
/// returned — an explicit predicate every implementation must apply, on top of whatever tenant
/// scoping the implementation already has. <c>null</c> adds no predicate of its own.
/// </param>
/// <param name="MaxResults">
/// Upper bound on the returned list; must be greater than zero. The cut is applied after ordering
/// by feature id (ordinal, ascending), so a truncated result is deterministic.
/// </param>
public sealed record AssetSpatialFilter(
    IReadOnlyCollection<string>? AssetTypeIds,
    Guid? OrganizationId,
    int MaxResults);
