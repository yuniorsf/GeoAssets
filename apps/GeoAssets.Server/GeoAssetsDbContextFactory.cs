using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoAssets.Server;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to generate migrations.
/// Not referenced at runtime — the production DbContext is created in Program.cs via
/// <see cref="GeoAssets.Provider.PostgreSQL.PostgresProviderFactory"/>.
/// </summary>
internal sealed class GeoAssetsDbContextFactory : IDesignTimeDbContextFactory<GeoAssetsDbContext>
{
    public GeoAssetsDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("GeoAssets")
            ?? throw new InvalidOperationException(
                "Connection string 'GeoAssets' not found in appsettings.json.");

        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, x => x.UseNetTopologySuite())
            .Options;

        return new GeoAssetsDbContext(options);
    }
}
