using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL;

/// <summary>
/// Builds <see cref="PostgresAssetProvider"/>s: the host's catalogs through <see cref="PostgresProviderPlugin"/>
/// (XD01-371), and one-off connections such as the GeoJSON importer's.
/// </summary>
public sealed class PostgresProviderFactory(ILoggerFactory loggerFactory, TimeProvider timeProvider)
    : IPostgresProviderFactory
{
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
    public Task<IAssetProvider> CreateAsync(string connectionString, CancellationToken ct = default) =>
        CreateAsync(connectionString, warmCache: true, ct);

    /// <summary>
    /// <see cref="CreateAsync(string, CancellationToken)"/>, migrating always and pre-warming the cache only when
    /// <paramref name="warmCache"/> is <see langword="true"/> — unwarmed, the first synchronous read loads it.
    /// </summary>
    public async Task<IAssetProvider> CreateAsync(string connectionString, bool warmCache, CancellationToken ct = default)
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
        if (warmCache) await provider.WarmCacheAsync(ct).ConfigureAwait(false);
        return provider;
    }
}
