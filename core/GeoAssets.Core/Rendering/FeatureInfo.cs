namespace GeoAssets.Core.Rendering;

/// <summary>The properties a map-click query (WMS GetFeatureInfo) returns for one feature (XD01-372).</summary>
public sealed record FeatureInfo(string Id, string Name, string AssetTypeId, string Description);
