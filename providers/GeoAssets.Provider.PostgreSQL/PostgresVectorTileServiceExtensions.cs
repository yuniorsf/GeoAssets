using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresVectorTileServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresVectorTileSource"/> as the host's <see cref="IVectorTileSource"/> (XD01-432), replacing
    /// any earlier registration: scoped, so each request's <see cref="ITenantContext"/> scopes its tiles, with the host's
    /// <see cref="ILodPolicy"/> when it registers one (else every layer is drawn as vector features at every zoom).
    /// </summary>
    public static IServiceCollection AddPostgresVectorTileSource(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // No EnableRetryOnFailure: each read runs in an explicit transaction, which a retrying strategy rejects.
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        services.RemoveAll<IVectorTileSource>();
        services.AddScoped<IVectorTileSource>(sp => new PostgresVectorTileSource(
            options,
            sp.GetService<ITenantContext>(),
            sp.GetService<ILodPolicy>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
