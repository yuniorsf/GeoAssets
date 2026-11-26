using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresFeatureImportServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresFeatureBulkImporter"/> as a singleton, both as itself and as
    /// <see cref="IFeatureBulkImporter"/>, over <paramref name="connectionString"/> — reloading an organization's cached
    /// catalog when an import completes if the host serves <see cref="PostgresOrganizationAssetCatalogs"/>. Each call opens
    /// its own context, so the singleton is safe to share; <see cref="TimeProvider.System"/> is added when no clock is
    /// registered. Committed writes report their dirty tiles to the host's <see cref="ITileInvalidation"/> when it registers
    /// one (XD01-432).
    /// </summary>
    public static IServiceCollection AddPostgresFeatureBulkImporter(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // No EnableRetryOnFailure: each chunk is one explicit transaction; a failed chunk is re-sent by its caller.
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new PostgresFeatureBulkImporter(
            options,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<PostgresOrganizationAssetCatalogs>(),
            sp.GetService<ITileInvalidation>(),
            sp.GetService<ILogger<PostgresFeatureBulkImporter>>()));
        services.AddSingleton<IFeatureBulkImporter>(sp => sp.GetRequiredService<PostgresFeatureBulkImporter>());
        return services;
    }
}
