using GeoAssets.Workflow.Persistence.Contracts;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The XD01-379 migration <c>AddTargetLedgerVertical</c> in the isolated FieldService history: <see cref="TargetLedgerMigrationContract"/>
/// — one body shared with the <c>GeoAssets.Server</c> history (<c>ServiceOrderMigrationChainTests</c> in GeoAssets.Security.Tests)
/// — run against this one.
/// </summary>
public partial class FieldServiceMigrationChainTests
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
        TargetLedgerMigrationContract.AssertRemoteRowsAreScopedToTheirOrganizationAsync(NewContextOnEmptyDatabaseAsync, OpenAsScopedRoleAsync);
}
