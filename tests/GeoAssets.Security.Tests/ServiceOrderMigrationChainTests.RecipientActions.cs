using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-336 migration <c>AddOrderTypeRecipientActions</c> in the <c>GeoAssets.Server</c>
/// history: the <c>"OrderTypeRecipientActions"</c> table and its seed for the shared-use notice, the
/// three functions, the orders' new update policy, and row-level security on
/// <c>"OrderActionLogs"</c>, which this history never had — up over existing rows, down and up
/// again. What the policies let each organization write is in
/// <see cref="RecipientActionsRowLevelSecurityTests"/>.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string RecipientActionsMigration = "AddOrderTypeRecipientActions";

    private const string ActionLogSecuritySql =
        """
        SELECT (relrowsecurity::text || ':' || coalesce((
                SELECT string_agg(policyname || '|' || cmd, ',' ORDER BY policyname COLLATE "C")
                FROM pg_policies WHERE schemaname = 'public' AND tablename = 'OrderActionLogs'), '-'))::text AS "Value"
        FROM pg_class WHERE oid = '"OrderActionLogs"'::regclass
        """;

    [Fact]
    public async Task RecipientActionsMigration_FollowsTheRecipientAccessOne_SeedsTheNotice_SecuresTheLog_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, RecipientActionsMigration);
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().Be(MigrationId(db, RecipientAccessMigration), "it adds to the recipient policies that migration left");

        // Before: an order with an entry in its log is stored; the log has no row-level security.
        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync(InsertOrderWithTwoTargetsSql);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "PerformedBy", "PerformedAt", "ActorKind")
            VALUES ('ord-1', 9, 'tester', now(), 0);
            """);
        (await QueryAsync(db, ActionLogSecuritySql)).Should().Equal("false:-");
        var policiesBefore = await QueryAsync(db, DispatchRecipientPoliciesSql);

        // Up.
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db,
            """
            SELECT ("OrderTypeId" || ':' || "Action"::text || ':' || coalesce("FromStateKey", '-') || ':' || coalesce("ToStateKey", '-'))::text AS "Value"
            FROM "OrderTypeRecipientActions" ORDER BY "Id"
            """))
            .Should().Equal(
                "shared-use-occupant-notice:4:-:-",
                "shared-use-occupant-notice:4:Pending:InProgress",
                "shared-use-occupant-notice:9:-:-");
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().Equal(
            "service_order_dispatched_to_current_organization|true|s|search_path=public, pg_temp",
            "service_order_dispatched_to_current_organization_with_access|true|s|search_path=public, pg_temp",
            "service_order_keeps_organization|true|s|search_path=public, pg_temp",
            "service_order_owned_by_or_granted_to_current_organization|true|s|search_path=public, pg_temp",
            "service_order_recipient_action_is_granted|true|s|search_path=public, pg_temp",
            "service_order_recipient_actions_granted|true|s|search_path=public, pg_temp",
            "service_order_recipient_change_is_granted|true|s|search_path=public, pg_temp",
            "service_order_target_stays_on_order|true|s|search_path=public, pg_temp");
        (await QueryAsync(db, ActionLogSecuritySql)).Should().Equal(
            "true:dispatch_recipient_insert_policy|INSERT,dispatch_recipient_select_policy|SELECT,tenant_isolation_policy|ALL");
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(
            policiesBefore.Append("ServiceOrders|dispatch_recipient_action_update_policy|PERMISSIVE|UPDATE").Order(StringComparer.Ordinal),
            "one policy more on the orders, and none changed");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrderActionLogs" """)).Should().Equal(["1"], "no row was touched");

        // Down.
        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
        (await QueryAsync(db, ActionLogSecuritySql)).Should().Equal("false:-");
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(policiesBefore);
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().HaveCount(5);
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM pg_tables WHERE tablename = 'OrderTypeRecipientActions'""")).Should().Equal("0");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrderActionLogs" """)).Should().Equal("1");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, ActionLogSecuritySql)).Should().ContainSingle().Which.Should().StartWith("true:");
    }
}
