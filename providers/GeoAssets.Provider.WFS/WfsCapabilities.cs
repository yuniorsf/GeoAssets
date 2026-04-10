namespace GeoAssets.Provider.WFS;

/// <summary>Parsed subset of a WFS 2.0 GetCapabilities response.</summary>
public sealed class WfsCapabilities
{
    public string Version { get; init; } = "2.0.0";
    public string Title   { get; init; } = string.Empty;
    public IReadOnlyList<WfsFeatureType> FeatureTypes { get; init; } = [];
}

/// <summary>A single feature type advertised by the WFS service.</summary>
public sealed class WfsFeatureType
{
    public string Name       { get; init; } = string.Empty;
    public string Title      { get; init; } = string.Empty;
    public string DefaultCrs { get; init; } = "urn:ogc:def:crs:EPSG::4326";
}
