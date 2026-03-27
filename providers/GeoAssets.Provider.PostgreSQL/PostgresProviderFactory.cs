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

public sealed class PostgresProviderFactory(ILoggerFactory loggerFactory)
    : IPostgresProviderFactory, IExternalProviderFactory
{
    public string ProviderName => "PostgreSQL";

    public IAssetProvider Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        var db     = new GeoAssetsDbContext(options);
        var logger = loggerFactory.CreateLogger<PostgresAssetProvider>();

        // Apply any pending migrations (idempotent)
        db.Database.Migrate();

        return new PostgresAssetProvider(db, logger);
    }
}
