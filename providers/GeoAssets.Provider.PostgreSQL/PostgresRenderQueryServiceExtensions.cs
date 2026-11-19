using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresRenderQueryServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresAssetRenderQuery"/> as the host's <see cref="IAssetRenderQuery"/> (XD01-372), replacing
    /// any earlier registration (e.g. the generic one over <see cref="IAssetProvider"/>): scoped, so each request's
    /// <see cref="ITenantContext"/> scopes its reads and its catalog (the request's <see cref="IAssetProvider"/>) styles them.
    /// </summary>
    public static IServiceCollection AddPostgresAssetRenderQuery(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // No EnableRetryOnFailure: each read runs in an explicit transaction, which a retrying strategy rejects.
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        services.RemoveAll<IAssetRenderQuery>();
        services.AddScoped<IAssetRenderQuery>(sp => new PostgresAssetRenderQuery(
            options,
            sp.GetService<ITenantContext>(),
            sp.GetRequiredService<IAssetProvider>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
