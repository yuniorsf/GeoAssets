using GeoAssets.Core.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// The registered <see cref="IProviderMapLayerRenderer"/>s, consulted in registration order
/// (XD01-422): the first whose <see cref="IProviderMapLayerRenderer.CanRender"/> accepts a provider
/// renders it. Registered by
/// <see cref="ProviderMapLayerRenderingServiceExtensions.AddProviderMapLayerRenderers"/>.
/// </summary>
public sealed class ProviderMapLayerRendererChain(IEnumerable<IProviderMapLayerRenderer> renderers)
{
    private readonly IProviderMapLayerRenderer[] _renderers = [.. renderers];

    /// <summary>The renderer for <paramref name="provider"/>, or <c>null</c> when none draws it
    /// (e.g. a provider declaring <see cref="ProviderRenderCapabilities.None"/>).</summary>
    public IProviderMapLayerRenderer? Resolve(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return Array.Find(_renderers, renderer => renderer.CanRender(provider));
    }
}
