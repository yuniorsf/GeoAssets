using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> over this host's own <see cref="ServiceOrderDbContext"/> (XD01-382) — the
/// <c>GeoAssets.FieldService.Server</c> pattern: migrations are generated into this assembly without building the host
/// (whose startup needs the kernel's URL and starts the registration service). <c>migrations add</c> never opens the
/// connection; <c>database update</c> reads <c>ConnectionStrings__NeutralOperator</c>.
/// </summary>
internal sealed class ServiceOrderDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ServiceOrderDbContext>
{
    public ServiceOrderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__NeutralOperator")
            ?? "Host=localhost;Port=5432;Database=geoassets_neutral_operator;Username=postgres;Password=YOUR_PASSWORD_HERE";

        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(NeutralOperatorServerPersistence.MigrationsAssembly))
            .Options;

        return new ServiceOrderDbContext(options);
    }
}
