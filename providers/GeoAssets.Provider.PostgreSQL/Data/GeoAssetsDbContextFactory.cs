using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.Provider.PostgreSQL.Data;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to generate migrations — next to the context since
/// XD01-372, so the Server need not reference this namespace. <c>dotnet ef</c> runs with this project as its own startup
/// project (XD01-374; <c>--project</c> and <c>--startup-project</c> both <c>providers/GeoAssets.Provider.PostgreSQL</c>), or
/// with the Server as before, whose <c>appsettings.json</c> it then reads from the current directory. Not referenced at runtime — the production
/// DbContext is created by <see cref="PostgresProviderFactory"/>.
/// </summary>
internal sealed class GeoAssetsDbContextFactory : IDesignTimeDbContextFactory<GeoAssetsDbContext>
{
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=geoassets;Username=postgres";

    public GeoAssetsDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Run from the Server project, its appsettings.json names the database; run from this project (XD01-374), the
        // ConnectionStrings__GeoAssets environment variable does, else a local default — enough for the commands that
        // never connect (migrations add/remove/has-pending-model-changes; list then reports it cannot connect).
        var connectionString = config.GetConnectionString("GeoAssets") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, x => x.UseNetTopologySuite())
            .Options;

        return new GeoAssetsDbContext(options);
    }
}
