using GeoAssets.Core.Interfaces;
using GeoAssets.Infrastructure.PostgreSQL.Data;
using GeoAssets.Infrastructure.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Infrastructure.PostgreSQL;

/// <summary>
/// Creates <see cref="IAssetRepository"/> instances backed by PostgreSQL + PostGIS.
/// Each call to <see cref="Create"/> opens a new <see cref="GeoAssetsDbContext"/>
/// pointing at the supplied connection string and applies any pending migrations automatically.
/// </summary>
public interface IPostgresRepositoryFactory
{
    /// <summary>
    /// Builds a connected <see cref="PostgresAssetRepository"/> for the given connection string.
    /// Throws on invalid connection strings or unreachable hosts.
    /// </summary>
    IAssetRepository Create(string connectionString);
}

public sealed class PostgresRepositoryFactory(ILoggerFactory loggerFactory)
    : IPostgresRepositoryFactory, IExternalRepositoryFactory
{
    public string ProviderName => "PostgreSQL";

    public IAssetRepository Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        var db     = new GeoAssetsDbContext(options);
        var logger = loggerFactory.CreateLogger<PostgresAssetRepository>();

        // Apply any pending migrations (idempotent)
        db.Database.Migrate();

        return new PostgresAssetRepository(db, logger);
    }
}
