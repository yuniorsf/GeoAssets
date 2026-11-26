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

    /// <summary>Whether this renderer draws <paramref name="provider"/> — decided from its
    /// <see cref="Core.Providers.AssetProviderExtensions.GetRenderCapabilities"/>, which looks
    /// through the decorator chain.</summary>
    bool CanRender(IAssetProvider provider);

    /// <summary>Draws the entry's whole layer; answers how many discrete features were drawn (0 for tiles).</summary>
    Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes the entry's layer off the map.</summary>
    Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draws what the entry shows inside <paramref name="viewport"/>, after the map's features were
    /// cleared for a pan; answers how many chunks or features were drawn, for the caller's log line.
    /// </summary>
    Task<int> RenderViewportAsync(MapLayerRenderRequest request, BoundingBox viewport, CancellationToken cancellationToken = default);
}
