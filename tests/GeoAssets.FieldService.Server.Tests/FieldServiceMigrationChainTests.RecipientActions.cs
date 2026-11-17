using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The XD01-336 migration <c>AddOrderTypeRecipientActions</c> in the
/// <c>GeoAssets.FieldService.Server</c> history: its table and seed, functions and policies, up,
/// down and up again; and — as a non-owner role — that the occupant of a shared-use notice accepts
/// it (Pending → InProgress) and comments on it, and writes nothing else of it or its log, while
/// the owner and a mirrored grantee write the order and its log as before. It also closes what
/// XD01-333 found open in this history: the log no longer follows its order to a recipient.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string RecipientActionsMigration = "AddOrderTypeRecipientActions";

    private const string ActionLogPoliciesSql =
        """
        SELECT (policyname || '|' || cmd || '|' || coalesce(qual, '') || '|' || coalesce(with_check, ''))::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'OrderActionLogs'
        ORDER BY policyname COLLATE "C"
        """;

    private const string RecipientActionRowsSql =
        """
        SELECT ("OrderTypeId" || ':' || "Action"::text || ':' || coalesce("FromStateKey", '-') || ':' || coalesce("ToStateKey", '-'))::text AS "Value"
        FROM "OrderTypeRecipientActions" ORDER BY "Id"
        """;

    private const string InsertActionLogSql =
        """
        INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "PerformedBy", "PerformedAt", "Comment", "ActorKind")
        VALUES (@orderId, @action, 'tester', now(), 'a comment', 0)
        """;

    [Fact]
    public async Task RecipientActionsMigration_SeedsTheNotice_AddsItsFunctionsAndPolicies_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, RecipientActionsMigration);
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().Be(MigrationId(db, RecipientAccessMigration));

        await migrator.MigrateAsync(previous);
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-1", OwnerOrganization);
            await ExecuteAsync(owner, InsertActionLogSql, ("orderId", "ord-1"), ("action", 9));
        }

        var logPoliciesBefore = await QueryAsync(db, ActionLogPoliciesSql);
        logPoliciesBefore.Should().ContainSingle().Which.Should().StartWith("tenant_isolation_policy|ALL|");
        var policiesBefore = await QueryAsync(db, DispatchRecipientPoliciesSql);

        // Up.
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, RecipientActionRowsSql)).Should().Equal(
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
        var logPolicies = await QueryAsync(db, ActionLogPoliciesSql);
        logPolicies.Should().HaveCount(3);
        // PostgreSQL prints the policies back with its own casts, so only the calls are matched.
        logPolicies[0].Should().StartWith("dispatch_recipient_insert_policy|INSERT|")
            .And.Contain("service_order_recipient_action_is_granted(").And.Contain("service_order_dispatched_to_current_organization_with_access(");
        logPolicies[1].Should().StartWith("dispatch_recipient_select_policy|SELECT|").And.Contain("service_order_dispatched_to_current_organization(");
        logPolicies[2].Should().StartWith("tenant_isolation_policy|ALL|").And.Contain("service_order_owned_by_or_granted_to_current_organization(");
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(
            policiesBefore.Append("ServiceOrders|dispatch_recipient_action_update_policy|PERMISSIVE|UPDATE").Order(StringComparer.Ordinal),
            "one policy more on the orders, and none changed");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrderActionLogs" """)).Should().Equal("1");

        // Down.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, ActionLogPoliciesSql)).Should().Equal(logPoliciesBefore);
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(policiesBefore);
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().HaveCount(5);
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM pg_tables WHERE tablename = 'OrderTypeRecipientActions'""")).Should().Equal("0");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrderActionLogs" """)).Should().Equal("1");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, ActionLogPoliciesSql)).Should().HaveCount(3);
        (await QueryAsync(db, RecipientActionRowsSql)).Should().HaveCount(3);
    }

    [Fact]
    public async Task RecipientActionsPolicies_AsANonOwnerRole_LetTheOccupantAcceptAndCommentOnItsNotice_AndNothingElse()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await SeedNoticesAsync(db);
        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        await SetOrganizationAsync(scoped, FirstRecipientOrganization);

        // The order: accept moves it from Pending to InProgress; a comment stamps "UpdatedAt"; nothing else.
        var cancel = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Status" = 'Cancelled' WHERE "Id" = 'notice-1'""");
        (await cancel.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation, "the notice grants no cancel");
        var retitle = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Status" = 'InProgress', "Title" = 'Mine' WHERE "Id" = 'notice-1'""");
        (await retitle.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation, "accepting changes nothing but the status");
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "UpdatedAt" = now() WHERE "Id" = 'notice-1'""")).Should().Be(1);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Status" = 'InProgress', "UpdatedAt" = now() WHERE "Id" = 'notice-1'""")).Should().Be(1);
        var complete = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Status" = 'Completed' WHERE "Id" = 'notice-1'""");
        (await complete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "UpdatedAt" = now() WHERE "Id" = 'inform-1'""")).Should().Be(0, "its type grants nothing");

        // The log: Accept and Annotate on the notice, anything on an Edit order, nothing else.
        (await AppendAsync(scoped, "notice-1", 4)).Should().Be(1);
        (await AppendAsync(scoped, "notice-1", 9)).Should().Be(1);
        (await AppendAsync(scoped, "notice-1", 8)).Should().Be(0, "Cancel");
        (await AppendAsync(scoped, "inform-1", 9)).Should().Be(0, "XD01-333's finding: the log no longer follows the order to its recipient");
        (await AppendAsync(scoped, "edit-1", 8)).Should().Be(1);
        (await ExecuteAsync(scoped, """UPDATE "OrderActionLogs" SET "Comment" = 'rewritten' WHERE "ServiceOrderId" = 'notice-1'""")).Should().Be(0);
        (await ExecuteAsync(scoped, """DELETE FROM "OrderActionLogs" WHERE "ServiceOrderId" = 'notice-1'""")).Should().Be(0);
        (await ReadStringsAsync(scoped, """SELECT "ServiceOrderId"::text AS "Value" FROM "OrderActionLogs" WHERE "ServiceOrderId" = 'notice-1'"""))
            .Should().HaveCount(2, "it reads the log of its notice");

        // Another occupant: nothing.
        await SetOrganizationAsync(scoped, SecondRecipientOrganization);
        (await AppendAsync(scoped, "notice-1", 9)).Should().Be(0);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "UpdatedAt" = now() WHERE "Id" = 'notice-1'""")).Should().Be(0);

        // The owner and a mirrored grantee: as before.
        await using (var owner = await OpenAsync(db))
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: "ServiceOrder", scopeId: null);
        foreach (var organization in new[] { OwnerOrganization, GranteeOrganization })
        {
            await SetOrganizationAsync(scoped, organization);
            (await AppendAsync(scoped, "notice-1", 8)).Should().Be(1);
            (await ExecuteAsync(scoped, """UPDATE "OrderActionLogs" SET "Comment" = 'by the owner side' WHERE "ServiceOrderId" = 'notice-1'""")).Should().BeGreaterThan(0);
            (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Status" = 'Cancelled', "Title" = 'Closed' WHERE "Id" = 'notice-1'""")).Should().Be(1);
        }

        await SetOrganizationAsync(scoped, OwnerOrganization);
        (await ExecuteAsync(scoped, """DELETE FROM "OrderActionLogs" WHERE "ServiceOrderId" = 'notice-1'""")).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// As the table owner: an Edit type, and three Pending orders of the owner dispatched to the first
    /// recipient organization — a shared-use notice (a type with no "OrderTypes" row: its actions are
    /// the migration's seed), an order of a type that grants nothing, and one of the Edit type.
    /// </summary>
    private static async Task SeedNoticesAsync(Workflow.Persistence.ServiceOrderDbContext db)
    {
        await using var owner = await OpenAsync(db);
        await ExecuteAsync(owner, """INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description", "RecipientAccess") VALUES ('rls-edit', 'Edit', '', 2)""");
        foreach (var (orderId, typeId) in new[] { ("notice-1", "shared-use-occupant-notice"), ("inform-1", "inspection"), ("edit-1", "rls-edit") })
        {
            await InsertOrderAsync(owner, orderId, OwnerOrganization);
            await ExecuteAsync(owner, """UPDATE "ServiceOrders" SET "OrderTypeId" = @type, "Status" = 'Pending' WHERE "Id" = @id""", ("type", typeId), ("id", orderId));
            await ExecuteAsync(owner, DispatchToOrganizationSql, ("orderId", orderId), ("targetId", FirstRecipientOrganization.ToString()), ("targetType", 2));
        }
    }

    /// <summary>Appends one entry of <paramref name="action"/>; a row-level security refusal reads as 0 rows.</summary>
    private static async Task<int> AppendAsync(NpgsqlConnection connection, string orderId, int action)
    {
        try
        {
            return await ExecuteAsync(connection, InsertActionLogSql, ("orderId", orderId), ("action", action));
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, RowSecurityViolation, StringComparison.Ordinal))
        {
            return 0;
        }
    }
}
