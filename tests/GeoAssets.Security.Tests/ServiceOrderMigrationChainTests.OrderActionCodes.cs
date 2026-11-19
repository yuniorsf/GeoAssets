using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-366 migration <c>AddOrderActionCodes</c> in the <c>GeoAssets.Server</c> history: a text code beside every order
/// action number, backfilled from it, the number columns made nullable, and the recipient insert policy comparing codes. What
/// is stored by a database that predates it must read identically after it, and the way back is refused while it would lose
/// an action.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string OrderActionCodesMigration = "AddOrderActionCodes";
    private const string BeforeOrderActionCodes = "AddOrderTypeCapabilities";
    private const string ActionsOrderId = "ac-ord";
    private const string ActionsTypeId = "ac-type";

    private static readonly string[] HistoricalCodes =
        ["View", "Approve", "Reject", "Assign", "Accept", "Dispatch", "Execute", "Complete", "Cancel", "Annotate", "Calculate", "Validate", "FlagClandestine", "Dispute"];

    private const string ActionColumnsSql =
        """
        SELECT (table_name || '.' || column_name || ':' || data_type || ':' || is_nullable)::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND ((table_name = 'OrderActionLogs' AND column_name IN ('Action', 'ActionCode'))
            OR (table_name = 'OrderActionPermissions' AND column_name IN ('Action', 'ActionCode'))
            OR (table_name = 'OrderTypeRecipientActions' AND column_name IN ('Action', 'ActionCode'))
            OR (table_name = 'OrderTypeTransitions' AND column_name IN ('TriggerAction', 'TriggerActionCode')))
        ORDER BY table_name COLLATE "C", column_name COLLATE "C"
        """;

    private const string GrantFunctionsSql =
        """
        SELECT pg_get_function_identity_arguments(oid)::text AS "Value" FROM pg_proc
        WHERE proname = 'service_order_recipient_action_is_granted' ORDER BY 1
        """;

    /// <summary>An order type and an order, as a database that predates the migration holds them: numbers only.</summary>
    private static async Task SeedNumberedActionsAsync(ServiceOrderDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description") VALUES ('ac-type', 'Action codes', '');
            INSERT INTO "OrderActionPermissions" ("OrderTypeId", "Action", "Kind", "Value", "FromStateKey") VALUES ('ac-type', 13, 1, 'dispute:run', 'Pending');
            INSERT INTO "OrderTypeTransitions" ("OrderTypeId", "FromStateKey", "ToStateKey", "TriggerAction") VALUES ('ac-type', 'Pending', 'Active', 12);
            INSERT INTO "OrderTypeRecipientActions" ("OrderTypeId", "Action", "FromStateKey", "ToStateKey") VALUES ('ac-type', 13, NULL, NULL);
            INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
            VALUES ('ac-ord', 'Order', '', 'ac-type', 'Pending', 0, 'tester', '00000000-0000-0000-0000-000000000000', now(), '{{}}', '[]');
            """);
        for (var number = 0; number < HistoricalCodes.Length; number++)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "PerformedBy", "PerformedAt", "Comment", "ActorKind")
                VALUES ('ac-ord', {0}, 'tester', '2026-01-15T10:00:00Z', {1}, 0)
                """,
                number, $"entry {number}");
        }
    }

    /// <summary>What the pre-change model read from the numbers: the enumeration value of each stored number, in row order.</summary>
    private static async Task<List<OrderActionType>> StoredActionNumbersAsync(ServiceOrderDbContext db) =>
        (await QueryAsync(db, """SELECT "Action"::text AS "Value" FROM "OrderActionLogs" WHERE "ServiceOrderId" = 'ac-ord' ORDER BY "Id" """))
            .Select(n => (OrderActionType)int.Parse(n, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    private static async Task<(IReadOnlyList<OrderAction> Log, IReadOnlyList<string> LogComments, OrderType Type)> ReadThroughTheRepositoriesAsync(ServiceOrderDbContext db)
    {
        await using var reading = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(db.Database.GetConnectionString()!));
        await using var orders = new EFServiceOrderRepository(reading);
        var order = await orders.GetByIdAsync(ActionsOrderId);
        var type = await new EFOrderTypeRepository(reading).GetByIdAsync(ActionsTypeId);
        return ([.. order!.ActionLog.Select(e => e.Action)], [.. order.ActionLog.Select(e => e.Comment ?? string.Empty)], type!);
    }

    [Fact]
    public async Task AddOrderActionCodesMigration_ExistingRows_ReadIdenticallyAfterMigration()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, OrderActionCodesMigration);
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().Be(MigrationId(db, BeforeOrderActionCodes), "the migration follows the capabilities one");

        // Before: the rows a deployed database holds today — numbers only, the schema the old model read.
        await migrator.MigrateAsync(previous);
        await SeedNumberedActionsAsync(db);
        (await QueryAsync(db, ActionColumnsSql)).Should().Equal(
            "OrderActionLogs.Action:integer:NO", "OrderActionPermissions.Action:integer:NO",
            "OrderTypeRecipientActions.Action:integer:NO", "OrderTypeTransitions.TriggerAction:integer:YES");
        var before = await StoredActionNumbersAsync(db);
        before.Select(a => (int)a).Should().Equal(Enumerable.Range(0, 14));

        // After: the same rows, read through the repositories, are the same actions the numbers named.
        await migrator.MigrateAsync(migration);
        var (log, comments, type) = await ReadThroughTheRepositoriesAsync(db);

        log.Should().Equal(before.Select(a => (OrderAction)a), "each stored number reads as the action it always was");
        log.Select(a => a.Code).Should().Equal(HistoricalCodes);
        comments.Should().Equal(Enumerable.Range(0, 14).Select(n => $"entry {n}"), "nothing else about an entry changed");
        type.ActionPermissions.Should().ContainSingle().Which.Should()
            .Be(new OrderActionPermission((OrderAction)(OrderActionType)13, PolicyKind.Permission, "dispute:run", "Pending"));
        type.Transitions.Should().ContainSingle().Which.Should()
            .Be(new WorkflowTransition("Pending", "Active", (OrderAction)(OrderActionType)12));
        type.RecipientActions.Should().Equal((OrderAction)(OrderActionType)13);

        // The codes were backfilled from the numbers, and the numbers stayed.
        (await QueryAsync(db, """SELECT ("Action"::text || ':' || "ActionCode")::text AS "Value" FROM "OrderActionLogs" ORDER BY "Id" """))
            .Should().Equal(HistoricalCodes.Select((code, number) => $"{number}:{code}"));
        (await QueryAsync(db, """SELECT ("Action"::text || ':' || "ActionCode")::text AS "Value" FROM "OrderActionPermissions" """)).Should().Equal("13:Dispute");
        (await QueryAsync(db, """SELECT ("TriggerAction"::text || ':' || "TriggerActionCode")::text AS "Value" FROM "OrderTypeTransitions" """)).Should().Equal("12:FlagClandestine");
        (await QueryAsync(db, """SELECT ("Action"::text || ':' || "ActionCode")::text AS "Value" FROM "OrderTypeRecipientActions" WHERE "OrderTypeId" = 'ac-type' """)).Should().Equal("13:Dispute");
        (await QueryAsync(db, """SELECT ("OrderTypeId" || ':' || "Action"::text || ':' || "ActionCode")::text AS "Value" FROM "OrderTypeRecipientActions" WHERE "OrderTypeId" = 'shared-use-occupant-notice' ORDER BY "Id" """))
            .Should().Equal("shared-use-occupant-notice:4:Accept", "shared-use-occupant-notice:4:Accept", "shared-use-occupant-notice:9:Annotate");
        (await QueryAsync(db, ActionColumnsSql)).Should().Equal(
            "OrderActionLogs.Action:integer:YES", "OrderActionLogs.ActionCode:character varying:YES",
            "OrderActionPermissions.Action:integer:YES", "OrderActionPermissions.ActionCode:character varying:YES",
            "OrderTypeRecipientActions.Action:integer:YES", "OrderTypeRecipientActions.ActionCode:character varying:YES",
            "OrderTypeTransitions.TriggerAction:integer:YES", "OrderTypeTransitions.TriggerActionCode:character varying:YES");
        (await QueryAsync(db, GrantFunctionsSql)).Should().Equal(
            "p_order_id character varying, p_action integer",
            "p_order_id character varying, p_action integer, p_action_code character varying");

        // Reading again, on a fresh context, gives the same: what was read once is what is stored.
        var (logAgain, _, typeAgain) = await ReadThroughTheRepositoriesAsync(db);
        logAgain.Should().Equal(log);
        typeAgain.Should().BeEquivalentTo(type);
    }

    [Fact]
    public async Task AddOrderActionCodesMigration_Down_RefusesWhileACodeOnlyActionExists()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var migration = MigrationId(db, OrderActionCodesMigration);
        var chain = db.Database.GetMigrations().ToList();
        var previous = chain[chain.IndexOf(migration) - 1];
        await migrator.MigrateAsync(previous);
        await SeedNumberedActionsAsync(db);
        await migrator.MigrateAsync(migration);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "ActionCode", "PerformedBy", "PerformedAt", "ActorKind")
            VALUES ('ac-ord', NULL, 'Survey', 'tester', now(), 0)
            """);

        var down = async () => await migrator.MigrateAsync(previous);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("cannot be reverted");
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(migration, "the refused way back left the migration applied");
        (await QueryAsync(db, ActionColumnsSql)).Should().Contain("OrderActionLogs.ActionCode:character varying:YES");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrderActionLogs" WHERE "ActionCode" = 'Survey' """)).Should().Equal(["1"], "nothing was lost");
        (await QueryAsync(db, GrantFunctionsSql)).Should().HaveCount(2, "the policy and its function were not touched");

        // Without the one action that has no number, the same way back goes through.
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "OrderActionLogs" WHERE "ActionCode" = 'Survey' """);
        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
    }

    [Fact]
    public async Task AddOrderActionCodesMigration_Down_RefusesWhileACodeOnlyTransitionTriggerExists()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var migration = MigrationId(db, OrderActionCodesMigration);
        var chain = db.Database.GetMigrations().ToList();
        var previous = chain[chain.IndexOf(migration) - 1];
        await migrator.MigrateAsync(previous);
        await SeedNumberedActionsAsync(db);
        await migrator.MigrateAsync(migration);
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "OrderTypeTransitions" ("OrderTypeId", "FromStateKey", "ToStateKey", "TriggerAction", "TriggerActionCode") VALUES ('ac-type', 'Active', 'Done', NULL, 'Survey')""");

        var down = async () => await migrator.MigrateAsync(previous);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("cannot be reverted");
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(migration);
    }

    [Fact]
    public async Task AddOrderActionCodesMigration_Down_WithOnlyNumberedActions_Restores()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var migration = MigrationId(db, OrderActionCodesMigration);
        var chain = db.Database.GetMigrations().ToList();
        var previous = chain[chain.IndexOf(migration) - 1];
        await migrator.MigrateAsync(previous);
        await SeedNumberedActionsAsync(db);
        await migrator.MigrateAsync(migration);
        // Written after the migration, by the new code: both columns filled, as a historical action is stored.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "ActionCode", "PerformedBy", "PerformedAt", "Comment", "ActorKind")
            VALUES ('ac-ord', 9, 'Annotate', 'tester', now(), 'after', 0)
            """);

        await migrator.MigrateAsync(previous);

        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
        (await QueryAsync(db, ActionColumnsSql)).Should().Equal(
            "OrderActionLogs.Action:integer:NO", "OrderActionPermissions.Action:integer:NO",
            "OrderTypeRecipientActions.Action:integer:NO", "OrderTypeTransitions.TriggerAction:integer:YES");
        (await QueryAsync(db, """SELECT "Action"::text AS "Value" FROM "OrderActionLogs" WHERE "ServiceOrderId" = 'ac-ord' ORDER BY "Id" """))
            .Should().Equal(Enumerable.Range(0, 14).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("9"), "every number is kept");
        (await QueryAsync(db, """SELECT "Action"::text AS "Value" FROM "OrderActionPermissions" """)).Should().Equal("13");
        (await QueryAsync(db, """SELECT "TriggerAction"::text AS "Value" FROM "OrderTypeTransitions" """)).Should().Equal("12");
        (await QueryAsync(db, GrantFunctionsSql)).Should().Equal(["p_order_id character varying, p_action integer"], "the three-argument function is gone");
        (await QueryAsync(db, """SELECT (pg_get_expr(polwithcheck, polrelid)) AS "Value" FROM pg_policy WHERE polname = 'dispatch_recipient_insert_policy' AND polrelid = '"OrderActionLogs"'::regclass"""))
            .Should().ContainSingle().Which.Should().Contain("service_order_recipient_action_is_granted(\"ServiceOrderId\", \"Action\")")
            .And.NotContain("ActionCode");

        // Up again: the codes come back from the numbers.
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, """SELECT "ActionCode" AS "Value" FROM "OrderActionLogs" ORDER BY "Id" """))
            .Should().Equal(HistoricalCodes.Append("Annotate"));
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
}
