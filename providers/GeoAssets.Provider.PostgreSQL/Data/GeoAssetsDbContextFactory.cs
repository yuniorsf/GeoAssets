using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.Provider.PostgreSQL.Data;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to generate migrations — next to the context since
/// XD01-372, so the Server need not reference this namespace; <c>dotnet ef</c> still runs with the Server as startup
/// project, whose <c>appsettings.json</c> it reads from the current directory. Not referenced at runtime — the production
/// DbContext is created by <see cref="PostgresProviderFactory"/>.
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
