namespace GeoAssets.Core.Interfaces;

/// <summary>
/// What an <see cref="IAssetProvider"/> can put on (and take off) a map, and which vector
/// actions its pool entry offers (ADR-002 D2, XD01-422). Read through
/// <see cref="Providers.AssetProviderExtensions.GetRenderCapabilities"/>, never by testing the
/// provider's concrete type at the call site.
/// </summary>
[Flags]
public enum ProviderRenderCapabilities
{
    /// <summary>Nothing to render and no vector actions.</summary>
    None = 0,

    /// <summary>Discrete features read through <see cref="IAssetProvider.GetAll"/> and the bounds queries.</summary>
    VectorFeatures = 1,

    /// <summary>Raster image tiles served by an endpoint (e.g. an OGC WMS — <see cref="IWmsProvider"/>).</summary>
    RasterTiles = 2,

    /// <summary>Vector tiles (MVT) served by an endpoint.</summary>
    VectorTiles = 4,

    /// <summary>Tiles below a zoom threshold, discrete features above it.</summary>
    HybridLod = 8,

    /// <summary>The features can be exported (GeoJSON, Shapefile).</summary>
    VectorExport = 16,

    /// <summary>Features can be imported into the provider (GeoJSON).</summary>
    VectorImport = 32,

    /// <summary>Features can be drawn and edited on the map.</summary>
    InteractiveEditing = 64,
}
