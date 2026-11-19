using GeoAssets.Workflow.Persistence.Contracts;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-380 against the <c>GeoAssets.Server</c> history: the kernel's <c>RemoveOccupancyFromKernelModel</c> and the NeutralOperator
/// chain's baseline that moves the occupancy tables, rows and row-level security included, into <c>neutral_operator</c>.
/// The assertions are <see cref="NeutralOperatorMigrationContract"/>'s, shared with the other history.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    [Fact]
    public Task NeutralOperatorBaseline_MovesTheOccupancyTablesWithRowsContentIndexesConstraintsAndPolicies() =>
        NeutralOperatorMigrationContract.AssertTheMoveKeepsRowsContentIndexesConstraintsAndPoliciesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorBaseline_AppliedAgain_IsANoOp() =>
        NeutralOperatorMigrationContract.AssertReapplyingTheBaselineIsANoOpAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorBaseline_WithAStrayTableInBothSchemas_RaisesAndChangesNothing() =>
        NeutralOperatorMigrationContract.AssertAStrayTableInBothSchemasRaisesAndChangesNothingAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorChain_WithoutTheKernelChain_Raises() =>
        NeutralOperatorMigrationContract.AssertTheChainWithoutTheKernelChainRaisesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorBaseline_OnAFreshDatabase_GivesEmptyTablesWithRowLevelSecurityAndPolicies() =>
        NeutralOperatorMigrationContract.AssertAFreshDatabaseGetsEmptyTablesWithRowLevelSecurityAndPoliciesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorBaseline_CreatePath_BuildsTheSameShapeAsTheMovedPath() =>
        NeutralOperatorMigrationContract.AssertTheCreatePathBuildsTheSameShapeAsTheMovedPathAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorBaseline_Down_MovesTheTablesBackWithTheirData_AndUpRestoresThem() =>
        NeutralOperatorMigrationContract.AssertDownMovesTheTablesBackWithTheirDataAndUpRestoresThemAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorBaseline_Down_RefusesWhilePublicHoldsASameNamedTable() =>
        NeutralOperatorMigrationContract.AssertDownRefusesWhilePublicHoldsASameNamedTableAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task NeutralOperatorTables_AfterTheMove_KeepTheirRowLevelSecurity_AsANonOwnerRole() =>
        NeutralOperatorMigrationContract.AssertRowLevelSecurityHoldsOnTheMovedTablesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task RemoveOccupancyFromKernelModelMigration_IsANoOpBothWays_AndTheKernelModelHasNoOccupancy() =>
        NeutralOperatorMigrationContract.AssertTheKernelMigrationIsANoOpAndTheKernelModelHasNoOccupancyAsync(NewContextOnEmptyDatabaseAsync);
}
