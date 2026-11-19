using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresServiceExtensions
{
    /// <summary>
    /// Registers the PostgreSQL repository factory (<see cref="PostgresProviderFactory"/>, also as
    /// <see cref="IPostgresProviderFactory"/>) and <see cref="PostgresProviderPlugin"/> as an
    /// <see cref="IProviderPlugin"/>, so a server's catalog pool can build catalogs with plugin
    /// <c>"postgres"</c> (XD01-371). For direct, short-lived DB contexts (e.g. the WMS renderer) see
    /// <see cref="AddGeoAssetsDbContextFactory"/>.
    /// </summary>
    public static IServiceCollection AddGeoAssetsPostgres(this IServiceCollection services)
    {
        services.AddSingleton<PostgresProviderFactory>();
        services.AddSingleton<IPostgresProviderFactory>(sp => sp.GetRequiredService<PostgresProviderFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderPlugin, PostgresProviderPlugin>());
        return services;
    }

    /// <summary>
    /// Registers a pre-warmed <see cref="IAssetProvider"/> singleton (wrapped in <see cref="ValidatingAssetProvider"/>)
    /// backed by PostgreSQL, so <c>PostgresAssetProvider</c>'s <see cref="ISyncProvider"/> marker is honest — no request
    /// can trigger <c>PostgresAssetProvider.LoadCacheFromDb</c>'s blocking fallback (XD01-173). The provider is built,
    /// migrated and warmed by <see cref="WarmPostgresAssetProviderAsync"/>, which the host awaits right after building
    /// its service provider and before it serves; resolving <see cref="IAssetProvider"/> earlier throws. Needs
    /// <see cref="AddGeoAssetsPostgres"/>, an <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/> and a
    /// <see cref="TimeProvider"/> registered.
    /// </summary>
    public static IServiceCollection AddWarmedPostgresAssetProvider(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(sp => new WarmedPostgresAssetProvider(sp.GetRequiredService<PostgresProviderFactory>(), connectionString));
        services.AddSingleton<IAssetProvider>(sp => sp.GetRequiredService<WarmedPostgresAssetProvider>().Provider);
        return services;
    }

    /// <summary>
    /// Builds, migrates and warms the provider <see cref="AddWarmedPostgresAssetProvider"/> registered. Call once, right
    /// after building the service provider and before anything reads assets; later calls do nothing.
    /// </summary>
    public static Task WarmPostgresAssetProviderAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<WarmedPostgresAssetProvider>().WarmAsync(ct);
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

    /// <summary>
    /// Registers <see cref="IAppliedMutationBatchStore"/> (XD01-229) over a <see cref="IDbContextFactory{GeoAssetsDbContext}"/>
    /// on <paramref name="connectionString"/> (<see cref="AddGeoAssetsDbContextFactory"/>): one scoped, per-request DbContext.
    /// </summary>
    public static IServiceCollection AddPostgresAppliedMutationBatchStore(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGeoAssetsDbContextFactory(connectionString);
        services.AddScoped<IAppliedMutationBatchStore>(sp =>
            new EFAppliedMutationBatchStore(sp.GetRequiredService<IDbContextFactory<GeoAssetsDbContext>>().CreateDbContext()));
        return services;
    }
}
