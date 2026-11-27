using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// One way of putting a pool entry's provider on the map (ADR-002 D2, XD01-422) — a strategy in
/// <see cref="ProviderMapLayerRendererChain"/>, which picks the first registered renderer whose
/// <see cref="CanRender"/> accepts the provider. A new kind of provider gets a new renderer
/// registered alongside <see cref="WmsMapLayerRenderer"/> and <see cref="VectorFeatureMapLayerRenderer"/>;
/// the map and the pool panel never test a provider's type.
/// </summary>
public interface IProviderMapLayerRenderer
{
    /// <summary>
    /// Whether <see cref="IMapInterop.ClearAllFeaturesAsync"/> also takes this renderer's layer off
    /// the map — true for discrete vector features, false for a tile layer, which therefore needs
    /// an explicit <see cref="RemoveAsync"/> when its entry is hidden.
    /// </summary>
    bool ClearedWithFeatures { get; }

    /// <summary>
    /// Where <see cref="ProviderMapLayerRendererChain"/> asks this renderer (XD01-436): a higher precedence is asked first,
    /// equal ones in registration order. A renderer that refines another one for some of its providers — as
    /// <see cref="HybridLodMapLayerRenderer"/> refines <see cref="VectorFeatureMapLayerRenderer"/> for a provider whose
    /// server serves tiles — declares <see cref="MapLayerRendererPrecedence.Refining"/>, so it wins whatever order the
    /// renderers were registered in. Defaults to <see cref="MapLayerRendererPrecedence.Default"/>.
    /// </summary>
    int Precedence => MapLayerRendererPrecedence.Default;

    /// <summary>Whether this renderer draws <paramref name="provider"/> — decided from its
    /// <see cref="Core.Providers.AssetProviderExtensions.GetRenderCapabilities"/>, which looks
    /// through the decorator chain.</summary>
    bool CanRender(IAssetProvider provider);

    /// <summary>Draws the entry's whole layer; answers how many discrete features were drawn (0 for tiles).</summary>
    Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes the entry's layer off the map.</summary>
    Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draws what the entry shows inside <paramref name="viewport"/> during a viewport pass
    /// (<see cref="IMapInterop.BeginViewportRenderAsync"/> … <see cref="IMapInterop.EndViewportRenderAsync"/>, XD01-429):
    /// the map is not cleared first, so a feature drawn again replaces its own layer and one the pass never draws is
    /// removed when the pass ends. Answers how many chunks or features were drawn, for the caller's log line. Once
    /// <paramref name="cancellationToken"/> is cancelled — a newer viewport superseded this one — nothing more is drawn.
    /// </summary>
    Task<int> RenderViewportAsync(MapLayerRenderRequest request, ViewportQuery viewport, CancellationToken cancellationToken = default);
}
