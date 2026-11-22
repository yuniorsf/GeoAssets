using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresReferenceFeatureQueryServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresReferenceFeatureQuery"/> as the host's <see cref="IReferenceFeatureQuery"/> (XD01-330),
    /// replacing any earlier registration (e.g. the generic one over the organization catalogs). A singleton: it reads
    /// under the tenant of the organization each call names, never the request's.
    /// </summary>
    public static IServiceCollection AddPostgresReferenceFeatureQuery(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // No EnableRetryOnFailure: each read runs in an explicit transaction, which a retrying strategy rejects.
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        services.RemoveAll<IReferenceFeatureQuery>();
        services.AddSingleton<IReferenceFeatureQuery>(new PostgresReferenceFeatureQuery(options));
        return services;
    }
}
