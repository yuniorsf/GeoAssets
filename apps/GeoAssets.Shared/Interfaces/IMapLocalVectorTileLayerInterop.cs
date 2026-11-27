using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Vector tile layers whose tiles are read on this device rather than from a network origin (XD01-460: an offline vector
/// map pack), drawn in <see cref="Services.MapRenderMode.MapLibre"/> as a vector source under the features. Each tile is
/// asked of the layer's <see cref="IVectorTileSource"/> from JS through a custom protocol (<c>geoassets-pack://</c>), so no
/// request leaves the device and the page's Content-Security-Policy needs no origin for it. Kept apart from
/// <see cref="IMapLocalTileLayerInterop"/>: a host registers it only for a map that draws vector tiles (MAUI in MapLibre
/// mode), and without it the provider's features are drawn instead.
/// </summary>
public interface IMapLocalVectorTileLayerInterop
{
    /// <summary>Adds the layer <paramref name="layerId"/> drawing <paramref name="layer"/>'s tiles from <paramref name="tiles"/>. Idempotent.</summary>
    Task AddLocalVectorTileLayerAsync(string divId, string layerId, IVectorTileSource tiles, LocalVectorTileLayer layer);

    /// <summary>Takes the layer <paramref name="layerId"/> off the map. Idempotent.</summary>
    Task RemoveLocalVectorTileLayerAsync(string divId, string layerId);
}
