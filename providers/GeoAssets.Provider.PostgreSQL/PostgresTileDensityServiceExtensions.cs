using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresTileDensityServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresTileDensitySampler"/> as the host's <see cref="ITileDensitySampler"/> (XD01-439),
    /// replacing any earlier registration: scoped, so each request's <see cref="ITenantContext"/> scopes what it measures.
    /// </summary>
    public static IServiceCollection AddPostgresTileDensitySampler(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // No EnableRetryOnFailure: each read runs in an explicit transaction, which a retrying strategy rejects.
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        services.RemoveAll<ITileDensitySampler>();
        services.AddScoped<ITileDensitySampler>(sp => new PostgresTileDensitySampler(
            options,
            sp.GetService<ITenantContext>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
