using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The XD01-316 migration <c>AddServiceOrderDispatchRecipientRowLevelSecurity</c> in the
/// <c>GeoAssets.FieldService.Server</c> history: which policies and functions it creates, up,
/// down and up again, and — as a non-owner role — what it lets the organization an order was
/// dispatched to do: read the order, its dispatches, its action log and its targets; append to
/// the log; and — the order being of a type that only informs it (XD01-333) — write neither the
/// order, nor a target, nor a dispatch.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string DispatchRecipientMigration = "AddServiceOrderDispatchRecipientRowLevelSecurity";
    private const string DispatchToOrganizationSql =
        """
        INSERT INTO "OrderDispatches" ("ServiceOrderId", "TargetId", "TargetType", "DispatchedBy", "DispatchedAt", "ActorKind")
        VALUES (@orderId, @targetId, @targetType, 'tester', now(), 0)
        """;

    private static readonly Guid FirstRecipientOrganization = Guid.Parse("c4c4c4c4-0000-0000-0000-000000000004");
    private static readonly Guid SecondRecipientOrganization = Guid.Parse("c5c5c5c5-0000-0000-0000-000000000005");

    /// <summary>Every policy of the tables the migration touches: <c>table|policy|kind|command</c>.</summary>
    private const string DispatchRecipientPoliciesSql =
        """
        SELECT (tablename || '|' || policyname || '|' || permissive || '|' || cmd)::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename IN ('ServiceOrders', 'ServiceOrderTargets', 'OrderDispatches')
        ORDER BY (tablename || '|' || policyname) COLLATE "C"
        """;

    private const string DispatchRecipientFunctionsSql =
        """
        SELECT (proname || '|' || prosecdef::text || '|' || provolatile::text || '|' || coalesce(array_to_string(proconfig, ','), ''))::text AS "Value"
        FROM pg_proc
        WHERE pronamespace = 'public'::regnamespace AND proname LIKE 'service\_order\_%'
        ORDER BY proname COLLATE "C"
        """;

    private const string DispatchesPolicyTextSql =
        """
        SELECT (policyname || '|' || cmd || '|' || coalesce(qual, '') || '|' || coalesce(with_check, ''))::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'OrderDispatches'
        ORDER BY policyname COLLATE "C"
        """;

    [Fact]
    public async Task DispatchRecipientMigration_IsTheNewestOfTheChain_AddsItsPoliciesAndFunctions_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, DispatchRecipientMigration);
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().Be(MigrationId(db, ChangeSetsRowLevelSecurityMigration));

        await migrator.MigrateAsync(previous);
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-1", OwnerOrganization);
            await InsertTargetAsync(owner, "tgt-1", "ord-1", OwnerOrganization);
            await ExecuteAsync(owner, DispatchToOrganizationSql, ("orderId", "ord-1"), ("targetId", FirstRecipientOrganization.ToString()), ("targetType", 2));
        }

        var dispatchesPolicyBefore = await QueryAsync(db, DispatchesPolicyTextSql);
        dispatchesPolicyBefore.Should().ContainSingle().Which.Should().StartWith("tenant_isolation_policy|ALL|");
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(
            "OrderDispatches|tenant_isolation_policy|PERMISSIVE|ALL",
            "ServiceOrderTargets|tenant_isolation_policy|PERMISSIVE|ALL",
            "ServiceOrders|tenant_isolation_policy|PERMISSIVE|ALL");

        // Up.
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(
            "OrderDispatches|dispatch_recipient_select_policy|PERMISSIVE|SELECT",
            "OrderDispatches|tenant_isolation_policy|PERMISSIVE|ALL",
            "ServiceOrderTargets|dispatch_recipient_select_policy|PERMISSIVE|SELECT",
            "ServiceOrderTargets|tenant_isolation_policy|PERMISSIVE|ALL",
            "ServiceOrders|dispatch_recipient_select_policy|PERMISSIVE|SELECT",
            "ServiceOrders|dispatch_recipient_update_policy|PERMISSIVE|UPDATE",
            "ServiceOrders|organization_is_fixed_policy|RESTRICTIVE|UPDATE",
            "ServiceOrders|tenant_isolation_policy|PERMISSIVE|ALL");
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().Equal(
            "service_order_dispatched_to_current_organization|true|s|search_path=public, pg_temp",
            "service_order_keeps_organization|true|s|search_path=public, pg_temp",
            "service_order_owned_by_or_granted_to_current_organization|true|s|search_path=public, pg_temp");
        (await QueryAsync(db, """SELECT pg_get_functiondef('service_order_owned_by_or_granted_to_current_organization(character varying)'::regprocedure)::text AS "Value" """))
            .Should().ContainSingle().Which.Should().Contain("\"OrganizationGrantMirror\"", "this database reads grants from its mirror");
        var dispatchesPolicies = await QueryAsync(db, DispatchesPolicyTextSql);
        dispatchesPolicies.Should().Contain(p => p.StartsWith("tenant_isolation_policy|ALL|", StringComparison.Ordinal)
            && p.Contains("service_order_owned_by_or_granted_to_current_organization", StringComparison.Ordinal),
            "a dispatch row now decides who sees an order, so only the owner or a grantee writes one");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrderDispatches" """)).Should().Equal("1");

        // Down: the dispatch rows follow their order again, the functions are gone, nothing is lost.
        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
        (await QueryAsync(db, DispatchesPolicyTextSql)).Should().Equal(dispatchesPolicyBefore);
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().HaveCount(3);
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().BeEmpty();
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM "OrderDispatches" """)).Should().Equal("1");

        // Up again — to this migration, whose policies are counted, then to the head (XD01-333's
        // migration adds a policy of its own).
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().HaveCount(8);
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchRecipientPolicy_AsANonOwnerRole_ShowsAnOccupantExactlyTheOrderDispatchedToIt_WithItsTargetsDispatchesAndLog()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await SeedFanOutAsync(db);
        await using var scoped = await OpenAsTenantScopedRoleAsync(db);

        await SetOrganizationAsync(scoped, FirstRecipientOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal(["child-a"], "not the parent, not the other occupant's child, not the order dispatched to a user spelled like it");
        (await ReadStringsAsync(scoped, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" ORDER BY "Id" """)).Should().Equal("tgt-child-a");
        (await ReadStringsAsync(scoped, """SELECT "ServiceOrderId"::text AS "Value" FROM "OrderDispatches" ORDER BY "Id" """)).Should().Equal("child-a");
        (await ReadStringsAsync(scoped, """SELECT "ServiceOrderId"::text AS "Value" FROM "OrderActionLogs" ORDER BY "Id" """)).Should().Equal("child-a");

        await SetOrganizationAsync(scoped, SecondRecipientOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal("child-b");

        await SetOrganizationAsync(scoped, OwnerOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal("child-a", "child-b", "parent", "to-user");

        await SetOrganizationAsync(scoped, StrangerOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchRecipientPolicy_AsANonOwnerRole_LetsTheOccupantOfAnInformOrderAppendToItsLog_AndWriteNothingElse()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await SeedFanOutAsync(db);
        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        await SetOrganizationAsync(scoped, FirstRecipientOrganization);

        // The order: its type has no row in "OrderTypes", so it only informs the occupant (XD01-333) —
        // not updated, not relabelled, not deleted. The log still follows its order.
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "AssignedTo" = 'occupant-crew' WHERE "Id" = 'child-a'""")).Should().Be(0);
        (await ExecuteAsync(scoped, OrderChildInserts["OrderActionLogs"], ("orderId", "child-a"))).Should().Be(1, "the log follows its order, which the occupant sees");
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "OrganizationId" = @org WHERE "Id" = 'child-a'""", ("org", FirstRecipientOrganization))).Should().Be(0);
        (await ExecuteAsync(scoped, """DELETE FROM "ServiceOrders" WHERE "Id" = 'child-a'""")).Should().Be(0);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "AssignedTo" = 'occupant-crew' WHERE "Id" = 'parent'""")).Should().Be(0);

        // Its targets: read-only.
        var addTarget = async () => await InsertTargetAsync(scoped, "tgt-forged", "child-a", OwnerOrganization, "feat-9");
        (await addTarget.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await ReleaseTargetsAsync(scoped, "child-a")).Should().Be(0);
        (await DeleteTargetAsync(scoped, "tgt-child-a")).Should().Be(0);

        // Its dispatches: it can pass the order on to nobody, and nobody can dispatch an order to itself.
        var passOn = async () => await ExecuteAsync(
            scoped, DispatchToOrganizationSql, ("orderId", "child-a"), ("targetId", StrangerOrganization.ToString()), ("targetType", 2));
        (await passOn.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await ExecuteAsync(scoped, """DELETE FROM "OrderDispatches" WHERE "ServiceOrderId" = 'child-a'""")).Should().Be(0);
        await SetOrganizationAsync(scoped, StrangerOrganization);
        var forge = async () => await ExecuteAsync(
            scoped, DispatchToOrganizationSql, ("orderId", "parent"), ("targetId", StrangerOrganization.ToString()), ("targetType", 2));
        (await forge.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await VisibleOrderIdsAsync(scoped)).Should().BeEmpty();

        // The owner is where it was: it writes all three, and cannot give an order away either.
        await SetOrganizationAsync(scoped, OwnerOrganization);
        await InsertTargetAsync(scoped, "tgt-owner-new", "child-a", OwnerOrganization, "feat-8");
        (await ExecuteAsync(scoped, DispatchToOrganizationSql, ("orderId", "to-user"), ("targetId", SecondRecipientOrganization.ToString()), ("targetType", 2))).Should().Be(1);
        var giveAway = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "OrganizationId" = @org WHERE "Id" = 'parent'""", ("org", StrangerOrganization));
        (await giveAway.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        await SetOrganizationAsync(scoped, SecondRecipientOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal(["child-b", "to-user"], "the owner's new dispatch reached its recipient");
    }

    [Fact]
    public async Task DispatchRecipientPolicy_AsANonOwnerRole_LeavesAMirroredGranteeWhereItWas_AndStopsItRelabellingAnOrder()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await SeedFanOutAsync(db);
        await using (var owner = await OpenAsync(db))
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: "ServiceOrder", scopeId: null);
        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        await SetOrganizationAsync(scoped, GranteeOrganization);

        (await VisibleOrderIdsAsync(scoped)).Should().Equal("child-a", "child-b", "parent", "to-user");
        await InsertTargetAsync(scoped, "tgt-grantee-new", "parent", OwnerOrganization, "feat-7");
        (await ExecuteAsync(scoped, DispatchToOrganizationSql, ("orderId", "parent"), ("targetId", "crew-1"), ("targetType", 1))).Should().Be(1);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Title" = 'By the grantee' WHERE "Id" = 'parent'""")).Should().Be(1);
        var relabel = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "OrganizationId" = @org WHERE "Id" = 'parent'""", ("org", GranteeOrganization));
        (await relabel.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be(RowSecurityViolation, "an order's organization is fixed for everyone, which also closes what XD01-323 found open to a grantee");
    }

    /// <summary>
    /// As the table owner: the pole owner's parent order; one child per occupant, each with a
    /// target, a log entry and a dispatch to its occupant; and one more order of the owner
    /// dispatched to a user whose id is spelled like the first occupant's organization id.
    /// </summary>
    private static async Task SeedFanOutAsync(Workflow.Persistence.ServiceOrderDbContext db)
    {
        await using var owner = await OpenAsync(db);
        foreach (var orderId in new[] { "parent", "child-a", "child-b", "to-user" })
        {
            await InsertOrderAsync(owner, orderId, OwnerOrganization);
            await ExecuteAsync(owner, OrderChildInserts["OrderActionLogs"], ("orderId", orderId));
        }

        await InsertTargetAsync(owner, "tgt-parent", "parent", OwnerOrganization);
        await InsertTargetAsync(owner, "tgt-child-a", "child-a", OwnerOrganization);
        await InsertTargetAsync(owner, "tgt-child-b", "child-b", OwnerOrganization, "feat-2");
        await ExecuteAsync(owner, DispatchToOrganizationSql, ("orderId", "child-a"), ("targetId", FirstRecipientOrganization.ToString()), ("targetType", 2));
        await ExecuteAsync(owner, DispatchToOrganizationSql, ("orderId", "child-b"), ("targetId", SecondRecipientOrganization.ToString().ToUpperInvariant()), ("targetType", 2));
        await ExecuteAsync(owner, DispatchToOrganizationSql, ("orderId", "to-user"), ("targetId", FirstRecipientOrganization.ToString()), ("targetType", 0));
    }
}
