using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresServiceExtensions
{
    /// <summary>
    /// Registers the PostgreSQL repository factory as both
    /// <see cref="IPostgresProviderFactory"/> and <see cref="IExternalProviderFactory"/>
    /// so the pool panel discovers it automatically.
    ///
    /// Also registers <see cref="IDbContextFactory{GeoAssetsDbContext}"/> so that
    /// services requiring direct, short-lived DB contexts per request (e.g. the WMS
    /// renderer) can create them safely from DI.
    /// </summary>
    public static IServiceCollection AddGeoAssetsPostgres(this IServiceCollection services)
    {
        services.AddSingleton<PostgresProviderFactory>();
        services.AddSingleton<IPostgresProviderFactory>(sp => sp.GetRequiredService<PostgresProviderFactory>());
        services.AddSingleton<IExternalProviderFactory>(sp => sp.GetRequiredService<PostgresProviderFactory>());
        return services;
    }

    /// <summary>
    /// Registers a fully pre-warmed <see cref="IAssetProvider"/> singleton (wrapped in
    /// <see cref="ValidatingAssetProvider"/>) backed by PostgreSQL, so <c>PostgresAssetProvider</c>'s
    /// <see cref="ISyncProvider"/> marker is actually honest for the caller's deployment — no
    /// request can trigger <c>PostgresAssetProvider.LoadCacheFromDb</c>'s blocking fallback
    /// (XD01-173).
    ///
    /// Must be called after <see cref="AddGeoAssetsPostgres"/> and after registering a
    /// <see cref="TimeProvider"/>, since it needs <see cref="PostgresProviderFactory"/>
    /// resolvable right away: <paramref name="services"/> isn't resolvable as a container until
    /// the caller's own host builder returns from <c>Build()</c>, but this registration needs
    /// the already-warmed instance at registration time — so it builds a throwaway interim
    /// container from the registrations made so far to resolve the factory and await
    /// <see cref="PostgresProviderFactory.CreateAsync"/> before registering the real singleton.
    /// That interim container is never validated (<c>BuildServiceProvider()</c>'s own
    /// <c>ServiceProviderOptions</c> default to <c>ValidateOnBuild=false</c>/<c>ValidateScopes=false</c>,
    /// independent of whatever the caller's real <c>Build()</c> applies later for the full,
    /// by-then-complete registration set), so it can't fail on registrations that land after
    /// this call.
    /// </summary>
    public static async Task<IServiceCollection> AddWarmedPostgresAssetProviderAsync(
        this IServiceCollection services, string connectionString, CancellationToken ct = default)
    {
#pragma warning disable ASP0000 // deliberate — see doc comment above
        var interim = services.BuildServiceProvider();
#pragma warning restore ASP0000
        try
        {
            var warmed = await interim
                .GetRequiredService<PostgresProviderFactory>()
                .CreateAsync(connectionString, ct)
                .ConfigureAwait(false);

            // Registered via a factory (not a pre-built instance) so the DI container takes
            // ownership and disposes it — a singleton instance handed to AddSingleton(instance)
            // is never disposed by the container.
            services.AddSingleton<IAssetProvider>(_ => new ValidatingAssetProvider(warmed));
            return services;
        }
        finally
        {
            await interim.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Registers <see cref="IDbContextFactory{GeoAssetsDbContext}"/> bound to the given
    /// <paramref name="connectionString"/>. Call this once in the host before
    /// registering services that need direct PostGIS access (e.g. the WMS renderer).
    /// </summary>
    public static IServiceCollection AddGeoAssetsDbContextFactory(
        this IServiceCollection services,
        string                  connectionString)
    {
        services.AddDbContextFactory<GeoAssetsDbContext>(opts =>
            opts.UseNpgsql(connectionString, npgsql => npgsql
                .UseNetTopologySuite()
                .EnableRetryOnFailure()));
        return services;
    }
}
