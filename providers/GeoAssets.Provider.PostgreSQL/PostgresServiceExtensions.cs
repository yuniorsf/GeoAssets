using GeoAssets.Core.Interfaces;
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
    /// Registers <see cref="IDbContextFactory{GeoAssetsDbContext}"/> bound to the given
    /// <paramref name="connectionString"/>. Call this once in the host before
    /// registering services that need direct PostGIS access (e.g. the WMS renderer).
    /// </summary>
    public static IServiceCollection AddGeoAssetsDbContextFactory(
        this IServiceCollection services,
        string                  connectionString)
    {
        services.AddDbContextFactory<GeoAssetsDbContext>(opts =>
            opts.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
        return services;
    }
}
