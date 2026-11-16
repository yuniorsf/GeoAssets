namespace GeoAssets.Core.Interfaces;

/// <summary>Reserved attribute keys understood by every <see cref="IAssetSpatialQuery"/> implementation.</summary>
public static class AssetSpatialQueryKeys
{
    /// <summary>
    /// Matches <see cref="Models.GeoFeature.Id"/> rather than an entry of
    /// <see cref="Models.GeoFeatureProperties.CustomAttributes"/>. Compared with ordinal, case-sensitive equality.
    /// </summary>
    public const string FeatureId = "id";
}
