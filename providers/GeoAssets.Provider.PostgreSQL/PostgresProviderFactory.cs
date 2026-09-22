using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL;

/// <summary>
/// Creates <see cref="IAssetProvider"/> instances backed by PostgreSQL + PostGIS.
/// Each call to <see cref="Create"/> opens a new <see cref="GeoAssetsDbContext"/>
/// pointing at the supplied connection string and applies any pending migrations automatically.
/// </summary>
public interface IPostgresProviderFactory
{
    /// <summary>
    /// Builds a connected <see cref="PostgresAssetProvider"/> for the given connection string.
    /// Throws on invalid connection strings or unreachable hosts.
    /// </summary>
    IAssetProvider Create(string connectionString);
}

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

        await db.Database.MigrateAsync(ct);

        var provider = new PostgresAssetProvider(db, options, logger, timeProvider);
        await provider.WarmCacheAsync(ct);
        return provider;
    }
}
