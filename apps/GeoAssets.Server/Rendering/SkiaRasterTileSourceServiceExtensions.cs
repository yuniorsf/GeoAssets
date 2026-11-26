using GeoAssets.Core.Rendering;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server.Rendering;

public static class SkiaRasterTileSourceServiceExtensions
{
    /// <summary>
    /// Registers <see cref="SkiaRasterTileSource"/> as the host's <see cref="IRasterTileSource"/> (XD01-433) unless one is
    /// registered already: scoped, over the request's <see cref="IAssetRenderQuery"/> (so its tenant scope and its
    /// provider's pushdown), with the host's <see cref="ILodPolicy"/> when it registers one. A later registration (e.g. a
    /// module's own raster source) wins.
    /// </summary>
    public static IServiceCollection AddSkiaRasterTileSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IRasterTileSource>(sp => new SkiaRasterTileSource(
            sp.GetRequiredService<IAssetRenderQuery>(),
            sp.GetService<ILodPolicy>()));
        return services;
    }
}
