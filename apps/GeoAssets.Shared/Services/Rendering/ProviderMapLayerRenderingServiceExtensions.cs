using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>Registration of the map's provider layer renderers (XD01-422).</summary>
public static class ProviderMapLayerRenderingServiceExtensions
{
    /// <summary>
    /// Registers <see cref="HybridLodMapLayerRenderer"/>, <see cref="WmsMapLayerRenderer"/> and
    /// <see cref="VectorFeatureMapLayerRenderer"/> as <see cref="IProviderMapLayerRenderer"/>s and the
    /// <see cref="ProviderMapLayerRendererChain"/> over them, all scoped like the
    /// <see cref="Interfaces.IMapInterop"/> they draw through. The chain asks them by
    /// <see cref="IProviderMapLayerRenderer.Precedence"/> — the hybrid renderer first (XD01-436), whatever the order of
    /// these registrations. Needs the host's <see cref="Core.Services.FeatureRenderPipeline"/>; the hybrid renderer draws
    /// only once the host also registers <see cref="Interfaces.IMapTileLayerInterop"/>. Calling it twice registers
    /// nothing twice.
    /// </summary>
    public static IServiceCollection AddProviderMapLayerRenderers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderMapLayerRenderer, WmsMapLayerRenderer>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderMapLayerRenderer, VectorFeatureMapLayerRenderer>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderMapLayerRenderer, HybridLodMapLayerRenderer>());
        services.TryAddScoped<ProviderMapLayerRendererChain>();
        return services;
    }
}
