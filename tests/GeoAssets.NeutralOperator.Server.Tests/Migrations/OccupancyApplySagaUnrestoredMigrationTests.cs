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
/// <c>AddOccupancyApplySagaUnrestoredFeatureIds</c> (XD01-392) on Postgres: it adds one nullable text column to the sagas of a
/// database that already holds some without changing a row, its <c>Down</c> drops just that column, and the shipped model
/// snapshot matches the context (the probe diff is empty).
/// </summary>
public sealed class OccupancyApplySagaUnrestoredMigrationTests
{
    private const string PriorMigrationName = "AddOccupancyApplySagas";

    private static readonly Guid Org = TestIdentities.OwnerOrganization;

    [Fact]
    public async Task Up_OverADatabaseWithSagasOnFile_AddsTheNullableColumn_AndChangesNoExistingRow()
    {
        var connectionString = await AtPriorMigrationAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        var batch = Guid.NewGuid();
        await InsertSagaAsync(connectionString, batch, "ord-old", "Reverted");

        await vertical.Database.MigrateAsync();

        (await DatabaseProbe.ValuesAsync(connectionString, ColumnSql)).Should().Equal("text:YES");
        (await DatabaseProbe.ValuesAsync(connectionString, $"""
            SELECT ("State" || ':' || "ServiceOrderId" || ':' || coalesce("UnrestoredFeatureIdsJson", '<null>') || ':' || "ConflictingFeatureIdsJson")::text AS "Value"
            FROM neutral_operator.occupancy_apply_sagas WHERE "BatchId" = '{batch}'
            """)).Should().Equal("Reverted:ord-old:<null>:[]");
        (await DatabaseProbe.ValuesAsync(connectionString, """SELECT count(*)::text AS "Value" FROM neutral_operator.occupancy_apply_sagas""")).Should().Equal("1");
        (await vertical.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Down_DropsOnlyTheColumn_LeavingTheSagasAndTheirOtherColumnsIntact()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        var batch = Guid.NewGuid();
        await InsertSagaAsync(connectionString, batch, "ord-down", "Reverted", unrestoredJson: """["pole-1"]""");

        await MigrateToPriorAsync(vertical);

        (await DatabaseProbe.ValuesAsync(connectionString, ColumnSql)).Should().BeEmpty();
        (await DatabaseProbe.ValuesAsync(connectionString, """SELECT ("State" || ':' || "ServiceOrderId")::text AS "Value" FROM neutral_operator.occupancy_apply_sagas""")).Should().Equal("Reverted:ord-down");
        (await vertical.Database.GetAppliedMigrationsAsync()).Should().HaveCount(2);

        await vertical.Database.MigrateAsync();

        (await DatabaseProbe.ValuesAsync(connectionString, ColumnSql)).Should().Equal("text:YES");
        (await DatabaseProbe.ValuesAsync(connectionString, """SELECT coalesce("UnrestoredFeatureIdsJson", '<null>')::text AS "Value" FROM neutral_operator.occupancy_apply_sagas""")).Should().ContainSingle().Which.Should().Be("<null>", "the dropped column's values are gone, not guessed");
    }

    [Fact]
    public async Task Probe_TheContextAgainstTheShippedSnapshot_HasNoPendingModelChanges()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);

        vertical.Database.HasPendingModelChanges().Should().BeFalse("the migration added the column the model declares, and nothing else is pending");
        (await vertical.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    private const string ColumnSql = """
        SELECT (data_type || ':' || is_nullable)::text AS "Value" FROM information_schema.columns
        WHERE table_schema = 'neutral_operator' AND table_name = 'occupancy_apply_sagas' AND column_name = 'UnrestoredFeatureIdsJson'
        """;

    /// <summary>A new database with the host's kernel chain and the vertical's chain up to the saga tables, before the column.</summary>
    private static async Task<string> AtPriorMigrationAsync()
    {
        var connectionString = await NeutralOperatorDatabase.NewNameOnEmptyServerAsync();
        await using (var serviceOrders = NeutralOperatorDatabase.ServiceOrderContext(connectionString))
            await serviceOrders.Database.MigrateAsync();

        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);
        await MigrateToPriorAsync(vertical);
        return connectionString;
    }

    private static Task MigrateToPriorAsync(NeutralOperatorDbContext vertical)
    {
        var prior = vertical.Database.GetMigrations().Single(id => id.EndsWith("_" + PriorMigrationName, StringComparison.Ordinal));
        return vertical.GetService<IMigrator>().MigrateAsync(prior);
    }

    private static async Task InsertSagaAsync(string connectionString, Guid batchId, string orderId, string state, string? unrestoredJson = null)
    {
        await using var owner = await KernelTables.OpenAsOwnerAsync(connectionString);
        var hasColumn = unrestoredJson is not null;
        await using var command = owner.CreateCommand();
        var extraColumn = hasColumn ? ", \"UnrestoredFeatureIdsJson\"" : string.Empty;
        var extraValue = hasColumn ? ", @unrestored" : string.Empty;
        command.CommandText =
            "INSERT INTO neutral_operator.occupancy_apply_sagas (\"BatchId\", \"ServiceOrderId\", \"OrderTypeId\", \"Attempt\", \"PreviousStatus\", "
            + "\"ApprovedStatus\", \"RequestedBy\", \"RequestedAt\", \"LastRequestedAt\", \"RetryCount\", \"State\", \"ConflictingFeatureIdsJson\", "
            + "\"BatchJson\", \"OrganizationId\"" + extraColumn + ") VALUES (@batch, @order, 'shared-use-pole-work', 1, 'Completed', 'Approved', 'tester', "
            + "now(), now(), 0, @state, '[]', '{}', @org" + extraValue + ")";
        command.Parameters.Add(new NpgsqlParameter("batch", batchId));
        command.Parameters.Add(new NpgsqlParameter("order", orderId));
        command.Parameters.Add(new NpgsqlParameter("state", state));
        command.Parameters.Add(new NpgsqlParameter("org", Org));
        if (hasColumn)
            command.Parameters.Add(new NpgsqlParameter("unrestored", unrestoredJson));
        await command.ExecuteNonQueryAsync();
    }
}
