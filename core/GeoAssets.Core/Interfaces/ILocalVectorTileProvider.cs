using GeoAssets.Core.Rendering;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Implemented by an <see cref="IAssetProvider"/> whose features are also Mapbox Vector Tiles read on this device rather
/// than from a network origin (XD01-460: an offline vector map pack), so a map engine that draws vector tiles (MapLibre)
/// draws its tiles instead of reading its features. A shared contract, so a provider plugin's implementation and the
/// map's renderer (<c>LocalVectorTileMapLayerRenderer</c>, the one place that reads these members) meet in the default
/// load context. A map that can't draw vector tiles (Leaflet) keeps reading the provider's features.
/// </summary>
public interface ILocalVectorTileProvider
{
    /// <summary>Where the tiles are read from.</summary>
    IVectorTileSource VectorTiles { get; }

    /// <summary>How the map draws them.</summary>
    LocalVectorTileLayer VectorTileLayer { get; }
}
