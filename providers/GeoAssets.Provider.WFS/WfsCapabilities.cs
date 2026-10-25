namespace GeoAssets.Provider.WFS;

/// <summary>Parsed subset of a WFS 2.0 GetCapabilities response.</summary>
public sealed class WfsCapabilities
{
    public string Version { get; init; } = "2.0.0";
    public string Title   { get; init; } = string.Empty;
    public IReadOnlyList<WfsFeatureType> FeatureTypes { get; init; } = [];
}
