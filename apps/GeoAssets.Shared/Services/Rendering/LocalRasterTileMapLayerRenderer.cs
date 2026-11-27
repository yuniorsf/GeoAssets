using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// Renders a provider whose raster tiles are read on this device (<see cref="ProviderRenderCapabilities.RasterTiles"/> on
/// an <see cref="ILocalRasterTileProvider"/> — an offline raster map pack, XD01-465) as a basemap tile layer named after
/// its pool entry, through <see cref="IMapLocalTileLayerInterop"/>; a host that doesn't register it (the Web) draws none.
/// The tile layer survives <see cref="IMapInterop.ClearAllFeaturesAsync"/> and reads its own tiles on pan, so a viewport
/// change draws nothing. The first time an entry is drawn on a map, the map fits the layer's bounds — the layer has no
/// features for the connect flow to fit to — so a newly connected pack is in view.
/// </summary>
public sealed class LocalRasterTileMapLayerRenderer(IMapInterop mapInterop, IMapLocalTileLayerInterop? tiles = null) : IProviderMapLayerRenderer
{
    private readonly HashSet<(string MapDivId, Guid EntryId)> _fitted = [];

    public bool ClearedWithFeatures => false;

    public bool CanRender(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return tiles is not null
            && provider.GetRenderCapabilities().HasFlag(ProviderRenderCapabilities.RasterTiles)
            && provider.UnwrapToConcrete() is ILocalRasterTileProvider;
    }

    public async Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var local = Local(request);
        var layer = local.RasterTileLayer;
        await Tiles(request).AddLocalRasterTileLayerAsync(request.MapDivId, request.LayerId, local.RasterTiles, layer);

        if (layer.Bounds is { } bounds && _fitted.Add((request.MapDivId, request.Entry.Id)))
            await mapInterop.FitBoundsAsync(request.MapDivId, [bounds.MinLon, bounds.MinLat, bounds.MaxLon, bounds.MaxLat]);
        return 0;
    }

    public Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Tiles(request).RemoveLocalRasterTileLayerAsync(request.MapDivId, request.LayerId);
    }

    public Task<int> RenderViewportAsync(MapLayerRenderRequest request, ViewportQuery viewport, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    private IMapLocalTileLayerInterop Tiles(MapLayerRenderRequest request) =>
        tiles ?? throw new InvalidOperationException($"This host draws no local raster tile layer (pool entry '{request.Entry.Name}').");

    // The layer belongs to the entry's own provider, not to whichever decorated view of it the caller reads through.
    private static ILocalRasterTileProvider Local(MapLayerRenderRequest request) =>
        request.Entry.Provider.UnwrapToConcrete() as ILocalRasterTileProvider
        ?? throw new InvalidOperationException($"Pool entry '{request.Entry.Name}' is not backed by a local raster tile provider.");
}
