using GeoAssets.Core.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// The registered <see cref="IProviderMapLayerRenderer"/>s (XD01-422), consulted by
/// <see cref="IProviderMapLayerRenderer.Precedence"/> — highest first — and, within one precedence, in registration
/// order (XD01-436): the first whose <see cref="IProviderMapLayerRenderer.CanRender"/> accepts a provider renders it.
/// Registered by <see cref="ProviderMapLayerRenderingServiceExtensions.AddProviderMapLayerRenderers"/>.
/// </summary>
public sealed class ProviderMapLayerRendererChain(IEnumerable<IProviderMapLayerRenderer> renderers)
{
    // OrderByDescending is stable: equal precedences keep their registration order.
    private readonly IProviderMapLayerRenderer[] _renderers = [.. renderers.OrderByDescending(r => r.Precedence)];

    /// <summary>The renderers in the order they are asked.</summary>
    public IReadOnlyList<IProviderMapLayerRenderer> Renderers => _renderers;

    /// <summary>The renderer for <paramref name="provider"/>, or <c>null</c> when none draws it
    /// (e.g. a provider declaring <see cref="ProviderRenderCapabilities.None"/>).</summary>
    public IProviderMapLayerRenderer? Resolve(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return Array.Find(_renderers, renderer => renderer.CanRender(provider));
    }
}
