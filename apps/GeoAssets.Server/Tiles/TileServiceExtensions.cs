using GeoAssets.Core.Rendering;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server.Tiles;

public static class TileServiceExtensions
{
    /// <summary>
    /// Registers what <see cref="TileEndpointExtensions.MapTileApi"/> serves with (XD01-434), all singletons:
    /// <see cref="TileServingOptions"/> from <c>GeoAssets:Tiles</c> (invalid values fail here, at startup), the
    /// <see cref="ILodPolicy"/> of those options unless the host registers its own, a <see cref="MemoryTileCache"/> of
    /// <see cref="TileServingOptions.CacheMaxBytes"/> as the <see cref="ITileCache"/> unless one is registered,
    /// <see cref="TileVersions"/> (under a random epoch) unless registered, <see cref="TileServing"/>, and <see cref="TileInvalidation"/> as the host's
    /// <see cref="ITileInvalidation"/> — the one the PostGIS writers report their committed writes to.
    /// </summary>
    public static IServiceCollection AddTileServing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = TileServingOptions.From(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.TryAddSingleton<ILodPolicy>(options.Lod);
        services.TryAddSingleton<ITileCache>(new MemoryTileCache(options.CacheMaxBytes));
        services.TryAddSingleton(_ => new TileVersions());
        services.AddSingleton(sp => new TileServing(
            options,
            sp.GetRequiredService<ILodPolicy>(),
            sp.GetRequiredService<ITileCache>(),
            sp.GetRequiredService<TileVersions>(),
            sp.GetRequiredService<TimeProvider>()));
        services.RemoveAll<ITileInvalidation>();
        services.AddSingleton<ITileInvalidation>(sp => new TileInvalidation(
            sp.GetRequiredService<ITileCache>(), sp.GetRequiredService<TileVersions>(), options));
        return services;
    }
}
