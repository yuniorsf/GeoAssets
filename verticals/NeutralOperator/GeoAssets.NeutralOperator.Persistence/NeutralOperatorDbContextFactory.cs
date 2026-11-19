using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling (XD01-380), with this project as both
/// <c>--project</c> and <c>--startup-project</c> — the pattern of <c>GeoAssetsDbContextFactory</c> (XD01-374). The
/// <c>ConnectionStrings__GeoAssets</c> environment variable names the database, else a local default: enough for the
/// commands that never connect (<c>migrations add/remove/has-pending-model-changes</c>). <c>database update</c> takes the
/// real one from that variable or from <c>--connection</c> — the kernel's database for GeoAssets.Server, the FieldService
/// database for GeoAssets.FieldService.Server. Not referenced at runtime: hosts build the context over the kernel's
/// connection (<see cref="NeutralOperatorPersistenceExtensions"/>).
/// </summary>
internal sealed class NeutralOperatorDbContextFactory : IDesignTimeDbContextFactory<NeutralOperatorDbContext>
{
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=geoassets;Username=postgres";

    public NeutralOperatorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GeoAssets") ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<NeutralOperatorDbContext>()
            .UseNpgsql(connectionString, NeutralOperatorDbContext.ConfigureMigrations)
            .Options;

        return new NeutralOperatorDbContext(options);
    }
}
