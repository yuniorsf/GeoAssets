using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresMutationBatchServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresAssetMutationBatchApplier"/> as a singleton, both as itself (so
    /// a host can decorate it) and as <see cref="IAssetMutationBatchApplier"/> (XD01-337), over
    /// <paramref name="connectionString"/>. Each batch opens its own context, so the singleton is
    /// safe to share; <see cref="TimeProvider.System"/> is added when no clock is registered.
    /// </summary>
    public static IServiceCollection AddPostgresAssetMutationBatchApplier(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // No EnableRetryOnFailure: each batch runs in an explicit transaction, which a retrying
        // execution strategy rejects. A failed batch is retried by whoever delivered it.
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new PostgresAssetMutationBatchApplier(
            options, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IAssetMutationBatchApplier>(sp => sp.GetRequiredService<PostgresAssetMutationBatchApplier>());
        return services;
    }
}
