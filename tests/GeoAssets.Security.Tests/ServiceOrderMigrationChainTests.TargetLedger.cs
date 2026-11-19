using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-379 migration <c>AddTargetLedgerVertical</c> in the <c>GeoAssets.Server</c> history: <see cref="TargetLedgerMigrationContract"/>
/// — one body shared with the isolated FieldService history — run against this one.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    [Fact]
    public Task AddTargetLedgerVerticalMigration_Up_BackfillsExistingRowsAndReshapesTheTable() =>
        TargetLedgerMigrationContract.AssertUpBackfillsExistingRowsAndReshapesTheTableAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task AddTargetLedgerVerticalMigration_TriggerDerivesKernelOrderId_AndCheckConstraintRejectsMismatches() =>
        TargetLedgerMigrationContract.AssertTriggerDerivesKernelOrderIdAndCheckConstraintRejectsMismatchesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task AddTargetLedgerVerticalMigration_Down_RefusesWhileRemoteRowsExist_ThenRestoresThePreviousShape() =>
        TargetLedgerMigrationContract.AssertDownRefusesWhileRemoteRowsExistAndOtherwiseRestoresThePreviousShapeAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task AddTargetLedgerVerticalMigration_RemoteRows_AreScopedToTheirOrganization_AsANonOwnerRole() =>
        TargetLedgerMigrationContract.AssertRemoteRowsAreScopedToTheirOrganizationAsync(NewContextOnEmptyDatabaseAsync, OpenScopedLedgerRoleAsync);

    /// <summary>A pooling-free connection as the suite's non-owner role, which holds exactly the privileges the target policies need.</summary>
    private static async Task<NpgsqlConnection> OpenScopedLedgerRoleAsync(ServiceOrderDbContext db)
    {
        await using (var owner = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await owner.OpenAsync();
            await using var grant = owner.CreateCommand();
            grant.CommandText = $"""
                GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderTargets" TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT ON "ServiceOrders" TO {SecurityTestContainerFixture.TenantScopedRole};
                GRANT SELECT ON "OrganizationGrants" TO {SecurityTestContainerFixture.TenantScopedRole};
                """;
            await grant.ExecuteNonQueryAsync();
        }

        await SecurityTestContainerFixture.EnsureStartedAsync();
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
