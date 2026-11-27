using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// Renders an OGC WMS provider (<see cref="ProviderRenderCapabilities.RasterTiles"/> on an
/// <see cref="IWmsProvider"/>) as a Leaflet WMS tile layer named after its pool entry (XD01-422).
/// The tile layer survives <see cref="IMapInterop.ClearAllFeaturesAsync"/> and fetches its own
/// tiles on pan, so a viewport change draws nothing. Both JS-side calls are idempotent (see
/// <c>geoassets.js</c>'s <c>addWmsLayer</c>/<c>removeWmsLayer</c>).
/// </summary>
public sealed class WmsMapLayerRenderer(IMapInterop mapInterop) : IProviderMapLayerRenderer
{
    public bool ClearedWithFeatures => false;

    public bool CanRender(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRenderCapabilities().HasFlag(ProviderRenderCapabilities.RasterTiles)
            && provider.UnwrapToConcrete() is IWmsProvider;
    }

    public async Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wms = Wms(request);
        await mapInterop.AddWmsLayerAsync(request.MapDivId, request.LayerId, wms.WmsBaseUrl,
            new WmsLayerOptions(Layers: wms.WmsLayerName, Format: wms.WmsFormat, TenantDomain: wms.WmsTenantDomain));
        return 0;
    }

    public Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return mapInterop.RemoveWmsLayerAsync(request.MapDivId, request.LayerId);
    }

    public Task<int> RenderViewportAsync(MapLayerRenderRequest request, ViewportQuery viewport, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    // The tile layer's parameters belong to the entry's own provider, not to whichever decorated
    // view of it the caller reads features through.
    private static IWmsProvider Wms(MapLayerRenderRequest request) =>
        request.Entry.Provider.UnwrapToConcrete() as IWmsProvider
        ?? throw new InvalidOperationException($"Pool entry '{request.Entry.Name}' is not backed by a WMS provider.");
}
