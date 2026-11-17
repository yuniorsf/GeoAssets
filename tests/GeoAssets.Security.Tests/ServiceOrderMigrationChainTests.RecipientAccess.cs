using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-333 migration <c>AddOrderTypeRecipientAccess</c> in the <c>GeoAssets.Server</c>
/// history: the <c>"OrderTypes"."RecipientAccess"</c> column, the two functions and the two
/// policies it restates or adds — up over existing rows, down and up again. What the policies let
/// each organization write is in <see cref="RecipientAccessRowLevelSecurityTests"/>.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string RecipientAccessMigration = "AddOrderTypeRecipientAccess";

    private const string RecipientAccessColumnSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default, ''))::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'OrderTypes' AND column_name = 'RecipientAccess'
        """;

    /// <summary>The two update policies the migration writes, with their expressions.</summary>
    private const string RecipientUpdatePoliciesSql =
        """
        SELECT (tablename || '|' || policyname || '|' || cmd || '|' || coalesce(qual, '') || '|' || coalesce(with_check, ''))::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND policyname = 'dispatch_recipient_update_policy'
        ORDER BY tablename COLLATE "C"
        """;

    [Fact]
    public async Task RecipientAccessMigration_FollowsTheDispatchRecipientPolicies_AddsItsColumnFunctionsAndPolicies_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, RecipientAccessMigration);
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().Be(MigrationId(db, DispatchRecipientMigration), "it restates a policy that migration created");

        // Before: an order type and a dispatched order of it are stored.
        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description") VALUES ('inspection', 'Inspection', '');
            """);
        await db.Database.ExecuteSqlRawAsync(InsertOrderWithTwoTargetsSql);
        var updatePolicyBefore = await QueryAsync(db, RecipientUpdatePoliciesSql);
        updatePolicyBefore.Should().ContainSingle().Which.Should().StartWith("ServiceOrders|dispatch_recipient_update_policy|UPDATE|");
        (await QueryAsync(db, RecipientAccessColumnSql)).Should().BeEmpty();

        // Up.
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, RecipientAccessColumnSql)).Should().Equal("RecipientAccess:integer:NO:0");
        (await QueryAsync(db, """SELECT ("Id" || ':' || "RecipientAccess"::text)::text AS "Value" FROM "OrderTypes" """))
            .Should().Equal(["inspection:0"], "a type stored before the column existed only informs its recipients");
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().Equal(
            "service_order_dispatched_to_current_organization|true|s|search_path=public, pg_temp",
            "service_order_dispatched_to_current_organization_with_access|true|s|search_path=public, pg_temp",
            "service_order_keeps_organization|true|s|search_path=public, pg_temp",
            "service_order_owned_by_or_granted_to_current_organization|true|s|search_path=public, pg_temp",
            "service_order_target_stays_on_order|true|s|search_path=public, pg_temp");
        var updatePolicies = await QueryAsync(db, RecipientUpdatePoliciesSql);
        updatePolicies.Should().HaveCount(2);
        // PostgreSQL prints the policies back with its own casts, so only the calls and levels are matched.
        updatePolicies[0].Should().StartWith("ServiceOrderTargets|dispatch_recipient_update_policy|UPDATE|")
            .And.Contain("service_order_dispatched_to_current_organization_with_access(").And.Contain(", 1)")
            .And.Contain("service_order_target_stays_on_order(")
            .And.Contain("service_order_keeps_organization(");
        updatePolicies[1].Should().StartWith("ServiceOrders|dispatch_recipient_update_policy|UPDATE|")
            .And.Contain("service_order_dispatched_to_current_organization_with_access(").And.EndWith(", 2)");
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().HaveCount(9, "one policy more than XD01-316 left");
        (await QueryAsync(db, StoredDispatchRowsSql)).Should().Equal(["ord-1:2:0"], "no row was touched");

        // Down: the column, the functions and the targets policy go; the orders policy is XD01-316's again.
        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
        (await QueryAsync(db, RecipientAccessColumnSql)).Should().BeEmpty();
        (await QueryAsync(db, RecipientUpdatePoliciesSql)).Should().Equal(updatePolicyBefore);
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().HaveCount(3);
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "OrderTypes" """)).Should().Equal("inspection");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, RecipientUpdatePoliciesSql)).Should().HaveCount(2);
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().HaveCount(5);
    }
}
