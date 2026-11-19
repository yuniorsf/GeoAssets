using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresSpatialQueryServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresAssetSpatialQuery"/> as the host's <see cref="IAssetSpatialQuery"/>
    /// (replacing any earlier registration of that port), so area/attribute selection is pushed
    /// down to PostGIS instead of scanning <c>PostgresAssetProvider</c>'s in-memory cache. Call it
    /// with the connection string of the host's PostgreSQL asset catalog.
    ///
    /// Scoped, so each request's own <see cref="ITenantContext"/> drives its tenant scoping; a host
    /// that registers no <see cref="ITenantContext"/> gets <see cref="NullTenantContext"/>, i.e.
    /// today's single-tenant rows. Hosts that never call this are untouched.
    /// </summary>
    public static IServiceCollection AddPostgresAssetSpatialQuery(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // No EnableRetryOnFailure: PostgresAssetSpatialQuery runs each read in an explicit
        // transaction, which a retrying execution strategy rejects.
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        services.RemoveAll<IAssetSpatialQuery>();
        services.AddScoped<IAssetSpatialQuery>(sp =>
            new PostgresAssetSpatialQuery(options, sp.GetService<ITenantContext>()));
        return services;
    }
}
