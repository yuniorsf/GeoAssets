using GeoAssets.Core.Rendering;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Implemented by an <see cref="IAssetProvider"/> whose map layer is raster image tiles read on this device rather than
/// from a network origin (XD01-465: an offline raster map pack), alongside
/// <see cref="ProviderRenderCapabilities.RasterTiles"/>. A shared contract, so a provider plugin's implementation and the
/// map's renderer (<c>LocalRasterTileMapLayerRenderer</c>, the one place that reads these members) meet in the default
/// load context.
/// </summary>
public interface ILocalRasterTileProvider
{
    /// <summary>Where the tiles are read from.</summary>
    IRasterTileSource RasterTiles { get; }

    /// <summary>How the map draws them.</summary>
    LocalRasterTileLayer RasterTileLayer { get; }
}
