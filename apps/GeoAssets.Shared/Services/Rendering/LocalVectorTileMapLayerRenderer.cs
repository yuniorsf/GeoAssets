using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// Renders a provider whose features are also vector tiles read on this device (an <see cref="ILocalVectorTileProvider"/>
/// with <see cref="ProviderRenderCapabilities.VectorFeatures"/> — an offline vector map pack, XD01-460) as a vector tile
/// layer named after its pool entry, through <see cref="IMapLocalVectorTileLayerInterop"/>, on a map that draws vector
/// tiles: it asks before <see cref="VectorFeatureMapLayerRenderer"/> (<see cref="MapLayerRendererPrecedence.Refining"/>),
/// so the pack's features are never read as well. A host that doesn't register the interop (the Web, MAUI in a Leaflet
/// mode) gets none of it, and <see cref="VectorFeatureMapLayerRenderer"/> draws the features as before. The tile layer
/// survives <see cref="IMapInterop.ClearAllFeaturesAsync"/> and reads its own tiles on pan, so a viewport change draws
/// nothing. The first time an entry is drawn on a map, the map fits the layer's bounds — the connect flow fits only to
/// features drawn — so a newly connected pack is in view. The tiles are drawn, not hit: the pack's features are neither
/// clicked, hovered nor snapped to in this mode.
/// </summary>
public sealed class LocalVectorTileMapLayerRenderer(IMapInterop mapInterop, IMapLocalVectorTileLayerInterop? tiles = null) : IProviderMapLayerRenderer
{
    private readonly HashSet<(string MapDivId, Guid EntryId)> _fitted = [];

    public bool ClearedWithFeatures => false;

    public int Precedence => MapLayerRendererPrecedence.Refining;

    public bool CanRender(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return tiles is not null
            && provider.GetRenderCapabilities().HasFlag(ProviderRenderCapabilities.VectorFeatures)
            && provider.UnwrapToConcrete() is ILocalVectorTileProvider;
    }

    public async Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var local = Local(request);
        var layer = local.VectorTileLayer;
        await Tiles(request).AddLocalVectorTileLayerAsync(request.MapDivId, request.LayerId, local.VectorTiles, layer);

        if (layer.Bounds is { } bounds && _fitted.Add((request.MapDivId, request.Entry.Id)))
            await mapInterop.FitBoundsAsync(request.MapDivId, [bounds.MinLon, bounds.MinLat, bounds.MaxLon, bounds.MaxLat]);
        return 0;
    }

    public Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Tiles(request).RemoveLocalVectorTileLayerAsync(request.MapDivId, request.LayerId);
    }

    public Task<int> RenderViewportAsync(MapLayerRenderRequest request, ViewportQuery viewport, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    private IMapLocalVectorTileLayerInterop Tiles(MapLayerRenderRequest request) =>
        tiles ?? throw new InvalidOperationException($"This host draws no local vector tile layer (pool entry '{request.Entry.Name}').");

    // The layer belongs to the entry's own provider, not to whichever decorated view of it the caller reads through.
    private static ILocalVectorTileProvider Local(MapLayerRenderRequest request) =>
        request.Entry.Provider.UnwrapToConcrete() as ILocalVectorTileProvider
        ?? throw new InvalidOperationException($"Pool entry '{request.Entry.Name}' is not backed by a local vector tile provider.");
}
