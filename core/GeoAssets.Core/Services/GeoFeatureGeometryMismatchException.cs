using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Thrown by <see cref="Providers.ValidatingAssetProvider"/> when a <see cref="Models.GeoFeature"/>'s
/// geometry shape doesn't match its asset type's <see cref="Models.AssetType.AllowedGeometryType"/>.
/// </summary>
public sealed class GeoFeatureGeometryMismatchException : InvalidOperationException
{
    public Guid AssetTypeId { get; }
    public GeometryType Expected { get; }
    public GeometryType Actual { get; }

    public GeoFeatureGeometryMismatchException(Guid assetTypeId, GeometryType expected, GeometryType actual)
        : base($"GeoFeature geometry type '{actual}' does not match asset type '{assetTypeId}'s allowed geometry type '{expected}'")
    {
        AssetTypeId = assetTypeId;
        Expected = expected;
        Actual = actual;
    }

    public GeoFeatureGeometryMismatchException()
    {
    }

    public GeoFeatureGeometryMismatchException(string message) : base(message)
    {
    }

    public GeoFeatureGeometryMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
