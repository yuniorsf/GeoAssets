using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>Registration of the map's provider layer renderers (XD01-422).</summary>
public static class ProviderMapLayerRenderingServiceExtensions
{
    /// <summary>
    /// Registers <see cref="WmsMapLayerRenderer"/> and <see cref="VectorFeatureMapLayerRenderer"/>
    /// (in that order) as <see cref="IProviderMapLayerRenderer"/>s and the
    /// <see cref="ProviderMapLayerRendererChain"/> over them, all scoped like the
    /// <see cref="Interfaces.IMapInterop"/> they draw through. Needs the host's
    /// <see cref="Core.Services.FeatureRenderPipeline"/>. Calling it twice registers nothing twice.
    /// </summary>
    public static IServiceCollection AddProviderMapLayerRenderers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderMapLayerRenderer, WmsMapLayerRenderer>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProviderMapLayerRenderer, VectorFeatureMapLayerRenderer>());
        services.TryAddScoped<ProviderMapLayerRendererChain>();
        return services;
    }
}
