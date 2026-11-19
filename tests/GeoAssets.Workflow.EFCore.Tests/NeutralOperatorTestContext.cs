using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The occupancy stores' <see cref="NeutralOperatorDbContext"/> (XD01-380) over a kernel test context's connection, scoped to
/// that context's organization and joining its transaction — the arrangement the hosts register.
/// </summary>
internal static class NeutralOperatorTestContext
{
    public static NeutralOperatorDbContext Over(ServiceOrderDbContext kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var connection = kernel.Database.GetDbConnection();
        var options = new DbContextOptionsBuilder<NeutralOperatorDbContext>();
        if (kernel.Database.IsSqlite())
            options.UseSqlite(connection);
        else
            options.UseNpgsql(connection, NeutralOperatorDbContext.ConfigureMigrations);

        return new NeutralOperatorDbContext(
            options.Options, new FakeTenantContext(kernel.CurrentOrganizationId), new ServiceOrderAmbientTransaction(kernel));
    }
}
