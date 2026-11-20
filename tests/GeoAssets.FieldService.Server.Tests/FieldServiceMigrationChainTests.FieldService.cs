using GeoAssets.Workflow.Persistence.Contracts;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// XD01-381 against the <c>GeoAssets.FieldService.Server</c> history: the kernel's <c>RemoveFieldServiceTablesFromKernelModel</c> (a
/// model-only migration here) and the FieldService chain's baseline that moves the five contractor-US / paving / manhole-audit tables,
/// rows and row-level security included, into <c>field_service</c>. The assertions are <see cref="FieldServiceMigrationContract"/>'s,
/// shared with the other history.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FieldServiceBaseline_MovesTheTablesWithRowsContentIndexesConstraintsAndPolicies(bool kernelHeadBeforeTheMove) =>
        FieldServiceMigrationContract.AssertTheMoveKeepsRowsContentIndexesConstraintsAndPoliciesAsync(NewContextOnEmptyDatabaseAsync, kernelHeadBeforeTheMove);

    [Fact]
    public Task FieldServiceBaseline_AppliedAgain_IsANoOp() =>
        FieldServiceMigrationContract.AssertReapplyingTheBaselineIsANoOpAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_WithAStrayTableInBothSchemas_RaisesAndChangesNothing() =>
        FieldServiceMigrationContract.AssertAStrayTableInBothSchemasRaisesAndChangesNothingAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceChain_WithoutTheKernelChain_Raises() =>
        FieldServiceMigrationContract.AssertTheChainWithoutTheKernelChainRaisesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_OnAFreshDatabase_GivesEmptyTablesWithRowLevelSecurityAndPolicies() =>
        FieldServiceMigrationContract.AssertAFreshDatabaseGetsEmptyTablesWithRowLevelSecurityAndPoliciesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_CreatePath_BuildsTheSameShapeAsTheMovedPath() =>
        FieldServiceMigrationContract.AssertTheCreatePathBuildsTheSameShapeAsTheMovedPathAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_Down_MovesTheTablesBackWithTheirData_AndUpRestoresThem() =>
        FieldServiceMigrationContract.AssertDownMovesTheTablesBackWithTheirDataAndUpRestoresThemAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_Down_RefusesWhilePublicHoldsASameNamedTable() =>
        FieldServiceMigrationContract.AssertDownRefusesWhilePublicHoldsASameNamedTableAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceTables_AfterTheMove_KeepTheirRowLevelSecurity_AsANonOwnerRole() =>
        FieldServiceMigrationContract.AssertRowLevelSecurityHoldsOnTheMovedTablesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceModels_OnMigratedDatabases_HaveNoPendingChanges_AndTheKernelModelHasNoFieldServiceEntities() =>
        FieldServiceMigrationContract.AssertTheKernelModelHasNoFieldServiceEntitiesAndBothModelsMatchTheirSnapshotsAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task RemoveFieldServiceTablesFromKernelModelMigration_IsANoOpBothWays_OverADatabaseWithData() =>
        FieldServiceMigrationContract.AssertTheKernelModelMigrationIsANoOpOverDataAsync(NewContextOnEmptyDatabaseAsync);
}
