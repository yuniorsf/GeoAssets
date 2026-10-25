using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL;

public sealed class PostgresProviderFactory(ILoggerFactory loggerFactory, TimeProvider timeProvider)
    : IPostgresProviderFactory, IAsyncProviderFactory
{
    public string ProviderName => "PostgreSQL";

    public IAssetProvider Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseNetTopologySuite()
                .EnableRetryOnFailure())
            .Options;

        var db     = new GeoAssetsDbContext(options);
        var logger = loggerFactory.CreateLogger<PostgresAssetProvider>();

        // Apply any pending migrations (idempotent)
        db.Database.Migrate();

        return new PostgresAssetProvider(db, options, logger, timeProvider);
    }

    /// <summary>
    /// Async counterpart to <see cref="Create"/> — migrates and pre-warms the cache before
    /// returning, so the provider's sync read surface (<c>GetAll</c>, <c>GetById</c>, etc.) never
    /// falls through to a blocking DB round trip on first use (XD01-161).
    /// </summary>
    public async Task<IAssetProvider> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseNetTopologySuite()
                .EnableRetryOnFailure())
            .Options;

        var db     = new GeoAssetsDbContext(options);
        var logger = loggerFactory.CreateLogger<PostgresAssetProvider>();

        await db.Database.MigrateAsync(ct).ConfigureAwait(false);

        var provider = new PostgresAssetProvider(db, options, logger, timeProvider);
        await provider.WarmCacheAsync(ct).ConfigureAwait(false);
        return provider;
    }
}
