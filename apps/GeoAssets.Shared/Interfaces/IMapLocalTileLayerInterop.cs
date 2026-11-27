using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Raster tile layers whose images are read on this device rather than from a network origin (XD01-465: an offline
/// raster map pack), drawn as a basemap — above the OpenStreetMap tiles, below every other tile layer and the features.
/// Each tile is asked of the layer's <see cref="IRasterTileSource"/> from JS — its bytes shown as a <c>blob:</c> object
/// URL in the Leaflet modes, a <c>geoassets-pack://</c> protocol answer in MapLibre mode (XD01-460) — so no request leaves
/// the device and the page's Content-Security-Policy needs no new origin. Kept apart from <see cref="IMapInterop"/>, as <see cref="IMapTileLayerInterop"/> is, so its
/// doubles stay untouched; a host that doesn't register it draws no such layer.
/// </summary>
public interface IMapLocalTileLayerInterop
{
    /// <summary>Adds the layer <paramref name="layerId"/> drawing <paramref name="layer"/>'s tiles from <paramref name="tiles"/>. Idempotent.</summary>
    Task AddLocalRasterTileLayerAsync(string divId, string layerId, IRasterTileSource tiles, LocalRasterTileLayer layer);

    /// <summary>Takes the layer <paramref name="layerId"/> off the map. Idempotent.</summary>
    Task RemoveLocalRasterTileLayerAsync(string divId, string layerId);
}
