using FluentAssertions;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Migrations;

/// <summary>
/// <c>AddOccupancyApplySagas</c> (XD01-383) on Postgres, on top of the rest of the chain: it is additive over a database that
/// already holds occupancy; its <c>Down</c> never drops data (it refuses, changing nothing, while a saga is on file or a change set
/// belongs to one) and goes through on an unused schema; and the saga poles' row-level security holds against a non-owner role.
/// </summary>
public sealed class OccupancyApplySagaMigrationTests
{
    private const string BaselineName = "AdoptOccupancyTables";

    private static readonly Guid OrgA = TestIdentities.OwnerOrganization;
    private static readonly Guid OrgB = TestIdentities.OtherOrganization;

    [Fact]
    public async Task Up_OverADatabaseAtTheBaseline_AddsTheSagaTablesColumnIndexesAndPolicy_AndChangesNoExistingRow()
    {
        var connectionString = await AtBaselineAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await SeedOrderWithAStagedSetAsync(connectionString, "ord-up", "tgt-up");

        await vertical.Database.MigrateAsync();

        (await Values(connectionString, """
            SELECT (c.column_name)::text AS "Value" FROM information_schema.columns c
            WHERE c.table_schema = 'neutral_operator' AND c.table_name = 'ServiceOrderOccupancyChangeSets' AND c.column_name = 'ApplyBatchId'
            """)).Should().ContainSingle();
        (await Values(connectionString, """
            SELECT ("Status" || ':' || coalesce("ApplyBatchId"::text, '-'))::text AS "Value" FROM neutral_operator."ServiceOrderOccupancyChangeSets"
            """)).Should().Equal("Staged:-");
        (await Values(connectionString, IndexNamesSql("occupancy_apply_sagas"))).Should().Equal(
            "IX_occupancy_apply_sagas_OrganizationId", "IX_occupancy_apply_sagas_ServiceOrderId_Attempt",
            "IX_occupancy_apply_sagas_State_LastRequestedAt", "PK_occupancy_apply_sagas");
        (await Values(connectionString, IndexNamesSql("occupancy_apply_saga_poles"))).Should().Equal(
            "IX_occupancy_apply_saga_poles_PoleFeatureId_OrganizationId", "PK_occupancy_apply_saga_poles");
        (await Values(connectionString, IndexNamesSql("ServiceOrderOccupancyChangeSets"))).Should().Contain("IX_ServiceOrderOccupancyChangeSets_ApplyBatchId");
        (await Values(connectionString, """
            SELECT (indexdef)::text AS "Value" FROM pg_indexes WHERE schemaname = 'neutral_operator' AND indexname = 'IX_occupancy_apply_sagas_ServiceOrderId_Attempt'
            """)).Single().Should().Contain("UNIQUE", "one saga per order and attempt");
        (await Values(connectionString, """
            SELECT (c.relname || ':' || c.relrowsecurity::text)::text AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'neutral_operator' AND c.relname IN ('occupancy_apply_sagas', 'occupancy_apply_saga_poles')
            """)).Should().BeEquivalentTo("occupancy_apply_sagas:false", "occupancy_apply_saga_poles:true");
        (await Values(connectionString, """
            SELECT (tablename || '|' || policyname || '|' || permissive || '|' || cmd)::text AS "Value" FROM pg_policies
            WHERE schemaname = 'neutral_operator' AND tablename LIKE 'occupancy_apply_saga%'
            """)).Should().Equal("occupancy_apply_saga_poles|tenant_isolation_policy|PERMISSIVE|ALL");
        (await vertical.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        vertical.Database.HasPendingModelChanges().Should().BeFalse("the shipped snapshot is the model");
    }

    [Fact]
    public async Task Up_AppliedTwice_ThroughTheMigrator_IsANoOpTheSecondTime()
    {
        var connectionString = await AtBaselineAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await vertical.Database.MigrateAsync();

        await vertical.Database.MigrateAsync();

        (await vertical.Database.GetAppliedMigrationsAsync()).Should().HaveCount(3);
        (await Values(connectionString, """SELECT count(*)::text AS "Value" FROM neutral_operator."__EFMigrationsHistory_neutral_operator" """)).Should().Equal("3");
    }

    [Fact]
    public async Task Down_WhileASagaIsOnFile_Refuses_ChangingNothing_AndSucceedsOnceItIsGone()
    {
        var connectionString = await AtBaselineAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await vertical.Database.MigrateAsync();
        await InsertSagaAsync(connectionString, Guid.NewGuid(), "ord-saga", OrgA);

        var down = DownFunc(vertical);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText
            .Should().Contain("Cannot drop neutral_operator.occupancy_apply_sagas").And.Contain("1 row(s)");
        (await vertical.Database.GetAppliedMigrationsAsync()).Should().HaveCount(2, "the step rolled back");
        (await Values(connectionString, """SELECT count(*)::text AS "Value" FROM neutral_operator.occupancy_apply_sagas""")).Should().Equal("1");

        await ExecuteAsync(connectionString, "DELETE FROM neutral_operator.occupancy_apply_sagas");
        await MigrateToBaselineAsync(vertical);

        (await vertical.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Reverted")]
    public async Task Down_WhileAChangeSetHoldsAStatusTheEarlierCodeDoesNotKnow_Refuses_ChangingNothing(string status)
    {
        var connectionString = await AtBaselineAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await SeedOrderWithAStagedSetAsync(connectionString, "ord-st", "tgt-st");
        await vertical.Database.MigrateAsync();
        await ExecuteAsync(connectionString, $"""UPDATE neutral_operator."ServiceOrderOccupancyChangeSets" SET "Status" = '{status}', "ClosedAt" = now()""");

        var down = DownFunc(vertical);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("change set(s) belong to an approval saga");
        (await vertical.Database.GetAppliedMigrationsAsync()).Should().HaveCount(2);
        (await Values(connectionString, """SELECT "Status"::text AS "Value" FROM neutral_operator."ServiceOrderOccupancyChangeSets" """)).Should().Equal(status);
    }

    [Fact]
    public async Task Down_WhileAChangeSetCarriesABatchId_Refuses_ChangingNothing()
    {
        var connectionString = await AtBaselineAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await SeedOrderWithAStagedSetAsync(connectionString, "ord-bt", "tgt-bt");
        await vertical.Database.MigrateAsync();
        await ExecuteAsync(connectionString, """UPDATE neutral_operator."ServiceOrderOccupancyChangeSets" SET "ApplyBatchId" = gen_random_uuid()""");

        var down = DownFunc(vertical);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("belong to an approval saga");
        (await vertical.Database.GetAppliedMigrationsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Down_OnAnUnusedSchema_DropsTheTablesAndTheColumn_AndUpRestoresThemWithTheOccupancyIntact()
    {
        var connectionString = await AtBaselineAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await SeedOrderWithAStagedSetAsync(connectionString, "ord-dn", "tgt-dn");
        await vertical.Database.MigrateAsync();

        await MigrateToBaselineAsync(vertical);

        (await Values(connectionString, """
            SELECT table_name::text AS "Value" FROM information_schema.tables
            WHERE table_schema = 'neutral_operator' AND table_name LIKE 'occupancy_apply_saga%'
            """)).Should().BeEmpty();
        (await Values(connectionString, """
            SELECT column_name::text AS "Value" FROM information_schema.columns
            WHERE table_schema = 'neutral_operator' AND table_name = 'ServiceOrderOccupancyChangeSets' AND column_name = 'ApplyBatchId'
            """)).Should().BeEmpty();
        (await Values(connectionString, """SELECT count(*)::text AS "Value" FROM neutral_operator."ServiceOrderOccupancyChangeSets" """)).Should().Equal("1");

        await vertical.Database.MigrateAsync();

        (await vertical.Database.GetAppliedMigrationsAsync()).Should().HaveCount(3);
        (await Values(connectionString, """SELECT count(*)::text AS "Value" FROM neutral_operator."ServiceOrderOccupancyChangeSets" """)).Should().Equal("1");
    }

    [Fact]
    public async Task SagaPoles_AsANonOwnerRole_AreSeenOnlyByTheirOrganization_AndAnotherOrganizationsRowIsRefused()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        var batch = Guid.NewGuid();
        await InsertSagaAsync(connectionString, batch, "ord-rls", OrgA);
        await using var scoped = await KernelTables.OpenAsScopedRoleAsync(connectionString);

        await KernelTables.SetOrganizationAsync(scoped, OrgA);
        await InsertSagaPoleAsync(scoped, batch, "pole-1", OrgA);
        (await CountAsync(scoped, "occupancy_apply_saga_poles")).Should().Be(1, "the owning organization sees its row");

        await KernelTables.SetOrganizationAsync(scoped, OrgB);
        (await CountAsync(scoped, "occupancy_apply_saga_poles")).Should().Be(0, "another organization sees none of it");
        var refused = await Assert.ThrowsAsync<PostgresException>(() => InsertSagaPoleAsync(scoped, batch, "pole-2", OrgA));
        refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "WITH CHECK refuses another organization's row");

        await KernelTables.SetOrganizationAsync(scoped, OrgA);
        (await CountAsync(scoped, "occupancy_apply_saga_poles")).Should().Be(1);
        (await CountAsync(scoped, "occupancy_apply_sagas")).Should().Be(1, "the saga table is not row-level secured: it holds no occupancy");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>A new database with the host's kernel chain and the vertical's chain up to its baseline.</summary>
    private static async Task<string> AtBaselineAsync()
    {
        var connectionString = await NeutralOperatorDatabase.NewNameOnEmptyServerAsync();
        await using (var serviceOrders = NeutralOperatorDatabase.ServiceOrderContext(connectionString))
            await serviceOrders.Database.MigrateAsync();

        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await MigrateToBaselineAsync(vertical);
        return connectionString;
    }

    private static Task MigrateToBaselineAsync(NeutralOperatorDbContext vertical)
    {
        var baseline = vertical.Database.GetMigrations().Single(id => id.EndsWith("_" + BaselineName, StringComparison.Ordinal));
        return vertical.GetService<IMigrator>().MigrateAsync(baseline);
    }

    /// <summary>The Down of the saga migration, as a call to make later (so a test can expect it to raise).</summary>
    private static Func<Task> DownFunc(NeutralOperatorDbContext vertical)
    {
        var migrator = vertical.GetService<IMigrator>();
        var baseline = vertical.Database.GetMigrations().Single(id => id.EndsWith("_" + BaselineName, StringComparison.Ordinal));
        return () => migrator.MigrateAsync(baseline);
    }

    private static Task<List<string>> Values(string connectionString, string sql) => DatabaseProbe.ValuesAsync(connectionString, sql);

    private static string IndexNamesSql(string table) =>
        $"""SELECT indexname::text AS "Value" FROM pg_indexes WHERE schemaname = 'neutral_operator' AND tablename = '{table}' ORDER BY indexname COLLATE "C" """;

    private static async Task SeedOrderWithAStagedSetAsync(string connectionString, string orderId, string targetId)
    {
        await using var owner = await KernelTables.OpenAsOwnerAsync(connectionString);
        await KernelTables.InsertOrderAsync(owner, orderId, OrgA);
        await KernelTables.InsertTargetAsync(owner, targetId, orderId, OrgA);
        await KernelTables.InsertChangeSetAsync(owner, orderId, targetId, OrgA);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var db = NeutralOperatorDatabase.ServiceOrderContext(connectionString);
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task InsertSagaAsync(string connectionString, Guid batchId, string orderId, Guid organizationId)
    {
        await using var owner = await KernelTables.OpenAsOwnerAsync(connectionString);
        await using var command = owner.CreateCommand();
        command.CommandText = """
            INSERT INTO neutral_operator.occupancy_apply_sagas
                ("BatchId", "ServiceOrderId", "OrderTypeId", "Attempt", "PreviousStatus", "ApprovedStatus", "RequestedBy", "RequestedAt", "LastRequestedAt",
                 "RetryCount", "State", "ConflictingFeatureIdsJson", "BatchJson", "OrganizationId")
            VALUES (@batch, @order, 'shared-use-pole-work', 1, 'Completed', 'Approved', 'tester', now(), now(), 0, 'Pending', '[]', '{}', @org)
            """;
        command.Parameters.Add(new NpgsqlParameter("batch", batchId));
        command.Parameters.Add(new NpgsqlParameter("order", orderId));
        command.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSagaPoleAsync(NpgsqlConnection connection, Guid batchId, string pole, Guid organizationId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO neutral_operator.occupancy_apply_saga_poles ("BatchId", "PoleFeatureId", "BaselineVersion", "AppliedVersion", "BaselineEntriesJson", "OrganizationId")
            VALUES (@batch, @pole, 0, 1, '[]', @org)
            """;
        command.Parameters.Add(new NpgsqlParameter("batch", batchId));
        command.Parameters.Add(new NpgsqlParameter("pole", pole));
        command.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        if (string.Equals(table, "occupancy_apply_sagas", StringComparison.Ordinal))
            command.CommandText = "SELECT count(*) FROM neutral_operator.occupancy_apply_sagas";
        else if (string.Equals(table, "occupancy_apply_saga_poles", StringComparison.Ordinal))
            command.CommandText = "SELECT count(*) FROM neutral_operator.occupancy_apply_saga_poles";
        else
            throw new ArgumentOutOfRangeException(nameof(table), table, "Unknown table.");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
