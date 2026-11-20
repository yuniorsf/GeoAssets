using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoAssets.FieldService.Persistence;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling (XD01-381), with this project as both
/// <c>--project</c> and <c>--startup-project</c> — the pattern of <c>NeutralOperatorDbContextFactory</c> (XD01-380). The
/// <c>ConnectionStrings__FieldService</c> environment variable names the database — the FieldService host's own, the one
/// <c>GeoAssets.FieldService.Server</c> reads from that same key — else a local default: enough for the commands that never
/// connect (<c>migrations add/remove/has-pending-model-changes</c>). <c>database update</c> takes the real one from that
/// variable or from <c>--connection</c>. Not referenced at runtime: the host builds the context over the kernel's
/// connection (<see cref="FieldServicePersistenceExtensions"/>).
/// </summary>
internal sealed class FieldServiceDbContextFactory : IDesignTimeDbContextFactory<FieldServiceDbContext>
{
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=geoassets_fieldservice;Username=postgres";

    public FieldServiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FieldService") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<FieldServiceDbContext>()
            .UseNpgsql(connectionString, FieldServiceDbContext.ConfigureMigrations)
            .Options;

        return new FieldServiceDbContext(options);
    }
}
