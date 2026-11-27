using GeoAssets.Core.Rendering;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server.Tiles;

public static class TileServiceExtensions
{
    /// <summary>
    /// Registers what <see cref="TileEndpointExtensions.MapTileApi"/> serves with (XD01-434), all singletons:
    /// <see cref="TileServingOptions"/> from <c>GeoAssets:Tiles</c> (invalid values fail here, at startup), the
    /// <see cref="ILodPolicy"/> and the vector tiles' <see cref="TileFeatureBudget"/> of those options unless the host registers its own, a <see cref="MemoryTileCache"/> of
    /// <see cref="TileServingOptions.CacheMaxBytes"/> as the <see cref="ITileCache"/> unless one is registered,
    /// <see cref="TileVersions"/> (under a random epoch) unless registered, <see cref="TileServing"/>, and <see cref="TileInvalidation"/> as the host's
    /// <see cref="ITileInvalidation"/> — the one the PostGIS writers report their committed writes to, which reads the writer's
    /// organization's grantees through an <c>IOrganizationGrantRepository</c> of a service scope of its own (XD01-458). Both use
    /// <see cref="TileVersionSync"/> when <see cref="AddTileVersionSharing"/> registered it; without it the versions are this
    /// process's alone, as before. <see cref="KeyedTileSources"/> (XD01-459) lists the plugins' keyed tile sources from these
    /// services once they are complete, its first resolution, and <see cref="KeyedTileSourceReport"/> logs them once the host starts.
    /// </summary>
    public static IServiceCollection AddTileServing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = TileServingOptions.From(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.TryAddSingleton<ILodPolicy>(options.Lod);
        services.TryAddSingleton(options.Budget);
        services.TryAddSingleton<ITileCache>(new MemoryTileCache(options.CacheMaxBytes));
        services.TryAddSingleton(_ => new TileVersions());
        services.TryAddSingleton(_ => KeyedTileSources.From(services));
        services.AddHostedService<KeyedTileSourceReport>();
        services.AddSingleton(sp => new TileServing(
            options,
            sp.GetRequiredService<ILodPolicy>(),
            sp.GetRequiredService<ITileCache>(),
            sp.GetRequiredService<TileVersions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<TileVersionSync>()));
        services.RemoveAll<ITileInvalidation>();
        services.AddSingleton<ITileInvalidation>(sp => new TileInvalidation(
            sp.GetRequiredService<ITileCache>(),
            sp.GetRequiredService<TileVersions>(),
            options,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<TileVersionSync>()));
        return services;
    }

    /// <summary>
    /// Shares the tile versions of <see cref="AddTileServing"/> with every kernel replica over the same database (XD01-458):
    /// <see cref="TileVersionSync"/> over the host's <see cref="ITileVersionStore"/> (which it must register — the
    /// <c>postgres-spatial</c> module's is PostgreSQL's) and the <see cref="TileVersionListener"/> hosted service that keeps
    /// it subscribed, with <paramref name="options"/> (default <see cref="TileVersionSyncOptions.Default"/>) unless registered.
    /// </summary>
    public static IServiceCollection AddTileVersionSharing(this IServiceCollection services, TileVersionSyncOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton((options ?? TileVersionSyncOptions.Default).Validated());
        services.TryAddSingleton(sp => new TileVersionSync(
            sp.GetRequiredService<ITileVersionStore>(),
            sp.GetRequiredService<TileVersions>(),
            sp.GetRequiredService<ITileCache>(),
            sp.GetRequiredService<TileServingOptions>(),
            sp.GetRequiredService<ILogger<TileVersionSync>>()));
        services.AddHostedService<TileVersionListener>();
        return services;
    }
}
