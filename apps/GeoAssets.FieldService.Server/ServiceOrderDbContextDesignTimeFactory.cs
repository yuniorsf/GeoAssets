using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c> tooling (XD01-290). Needed because this
/// host's full <c>Program.cs</c> fails EF's design-time service-provider validation for reasons
/// unrelated to <see cref="ServiceOrderDbContext"/> itself (e.g. <c>OutboxRelayProcessor</c>'s
/// and <c>BssOrderSpawnService</c>'s own pre-existing scoped/singleton DI shape, which only
/// surfaces under the host-validation EF's tooling performs, not at normal runtime) — this
/// factory lets migration generation bypass building that host entirely. Lives here rather than
/// in <c>GeoAssets.Workflow.EFCore</c> because that project is deliberately provider-agnostic
/// (see <see cref="ServiceOrderDbContext"/>'s own doc comment) and doesn't reference Npgsql.
///
/// The connection string here is never actually opened by <c>migrations add</c> (only the model
/// matters for that); it only matters for <c>database update</c>, where the real one should come
/// from the environment instead.
/// </summary>
internal sealed class ServiceOrderDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ServiceOrderDbContext>
{
    public ServiceOrderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FieldService")
            ?? "Host=localhost;Port=5432;Database=geoassets_fieldservice;Username=postgres;Password=YOUR_PASSWORD_HERE";

        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.FieldService.Server"))
            .Options;

        return new ServiceOrderDbContext(options);
    }
}
