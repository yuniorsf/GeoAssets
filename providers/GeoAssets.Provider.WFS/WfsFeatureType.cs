namespace GeoAssets.Provider.WFS;

/// <summary>A single feature type advertised by the WFS service.</summary>
public sealed class WfsFeatureType
{
    public string Name       { get; init; } = string.Empty;
    public string Title      { get; init; } = string.Empty;
    public string DefaultCrs { get; init; } = "urn:ogc:def:crs:EPSG::4326";
}
