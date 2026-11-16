using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Runs <see cref="ServiceOrderRowVersionContract"/> against the shared-database history carried
/// by <c>GeoAssets.Server</c> (XD01-323). <c>FieldServiceMigrationChainTests</c> in
/// GeoAssets.FieldService.Server.Tests runs the very same source file against the isolated
/// FieldService history, which is what proves optimistic concurrency on service orders behaves
/// the same in both databases.
///
/// On a database of its own inside <see cref="SecurityTestContainerFixture"/>'s container, like
/// <c>ServiceOrderMigrationChainTests</c>: the row-level-security facts truncate "ServiceOrders"
/// in the shared one.
/// </summary>
public sealed class ServiceOrderRowVersionParityTests
{
    [Fact]
    public async Task ServiceOrderRowVersion_OnTheSharedDatabaseChain_GuardsConcurrentWriters()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"service_order_row_version_{Guid.NewGuid():N}",
        }.ConnectionString;
        // "ServiceOrders"' policy in this history reads "OrganizationGrants", which GeoIdentityDbContext's chain creates.
        await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
        {
            await identity.Database.MigrateAsync();
        }

        var options = SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString);
        await using (var serviceOrders = new ServiceOrderDbContext(options))
        {
            await serviceOrders.Database.MigrateAsync();
        }

        await ServiceOrderRowVersionContract.AssertGuardsConcurrentWritersAsync(() => new ServiceOrderDbContext(options));
    }
}
