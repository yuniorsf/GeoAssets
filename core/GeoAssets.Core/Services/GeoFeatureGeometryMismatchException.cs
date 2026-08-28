using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Thrown by <see cref="Providers.ValidatingAssetProvider"/> when a <see cref="Models.GeoFeature"/>'s
/// geometry shape doesn't match its asset type's <see cref="Models.AssetType.AllowedGeometryType"/>.
/// </summary>
public sealed class GeoFeatureGeometryMismatchException(Guid assetTypeId, GeometryType expected, GeometryType actual)
    : InvalidOperationException(
        $"GeoFeature geometry type '{actual}' does not match asset type '{assetTypeId}'s allowed geometry type '{expected}'")
{
    public Guid AssetTypeId { get; } = assetTypeId;
    public GeometryType Expected { get; } = expected;
    public GeometryType Actual { get; } = actual;
}
