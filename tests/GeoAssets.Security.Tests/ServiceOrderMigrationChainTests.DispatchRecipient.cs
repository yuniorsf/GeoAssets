using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-316 migration <c>AddServiceOrderDispatchRecipientRowLevelSecurity</c> in the
/// <c>GeoAssets.Server</c> history: which policies and functions it creates, up, down and up
/// again, over rows that already exist. What the policies let each organization see and write
/// is in <see cref="DispatchRecipientRowLevelSecurityTests"/>.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string DispatchRecipientMigration = "AddServiceOrderDispatchRecipientRowLevelSecurity";

    /// <summary>Every policy of the three tables the migration touches: <c>table|policy|kind|command</c>.</summary>
    private const string DispatchRecipientPoliciesSql =
        """
        SELECT (tablename || '|' || policyname || '|' || permissive || '|' || cmd)::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename IN ('ServiceOrders', 'ServiceOrderTargets', 'OrderDispatches')
        ORDER BY (tablename || '|' || policyname) COLLATE "C"
        """;

    /// <summary>The migration's functions: <c>name|security definer|volatility|settings</c>.</summary>
    private const string DispatchRecipientFunctionsSql =
        """
        SELECT (proname || '|' || prosecdef::text || '|' || provolatile::text || '|' || coalesce(array_to_string(proconfig, ','), ''))::text AS "Value"
        FROM pg_proc
        WHERE pronamespace = 'public'::regnamespace AND proname LIKE 'service\_order\_%'
        ORDER BY proname COLLATE "C"
        """;

    /// <summary>The targets table's policies with their expressions; a policy with no WITH CHECK ends in an empty one.</summary>
    private const string TargetPolicyTextsSql =
        """
        SELECT (policyname || '|' || permissive || '|' || cmd || '|' || coalesce(qual, '') || '|' || coalesce(with_check, ''))::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'ServiceOrderTargets'
        ORDER BY policyname COLLATE "C"
        """;

    private const string DispatchesRowLevelSecuritySql =
        """
        SELECT (relrowsecurity::text || ':' || relforcerowsecurity::text)::text AS "Value"
        FROM pg_class
        WHERE oid = '"OrderDispatches"'::regclass
        """;

    private const string StoredDispatchRowsSql =
        """
        SELECT (o."Id" || ':' || (SELECT count(*) FROM "ServiceOrderTargets" t WHERE t."ServiceOrderId" = o."Id")::text
                || ':' || (SELECT count(*) FROM "OrderDispatches" d WHERE d."ServiceOrderId" = o."Id")::text)::text AS "Value"
        FROM "ServiceOrders" o
        ORDER BY o."Id"
        """;

    [Fact]
    public async Task DispatchRecipientMigration_IsTheNewestOfTheChain_AndAddsItsPoliciesAndFunctionsOverExistingRows_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, DispatchRecipientMigration);
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().Be(MigrationId(db, ChangeSetsRowLevelSecurityMigration), "the dispatch-recipient policies come after everything they refer to");

        // The schema every deployed database has today, with a dispatched order stored.
        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync(InsertOrderWithTwoTargetsSql);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "OrderDispatches" ("ServiceOrderId", "TargetId", "TargetType", "DispatchedBy", "DispatchedAt", "ActorKind")
            VALUES ('ord-1', '22222222-2222-2222-2222-222222222222', 2, 'tester', now(), 0)
            """);
        var targetsPolicyBefore = (await QueryAsync(db, TargetPolicySql)).Should().ContainSingle().Subject;
        (await QueryAsync(db, DispatchesRowLevelSecuritySql)).Should().Equal(["false:false"], "this history never protected the dispatch rows");
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(
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
            ["service_order_dispatched_to_current_organization|true|s|search_path=public, pg_temp",
             "service_order_keeps_organization|true|s|search_path=public, pg_temp",
             "service_order_owned_by_or_granted_to_current_organization|true|s|search_path=public, pg_temp"],
            "each is SECURITY DEFINER, STABLE and pinned to a search path nobody else can put objects ahead in");
        (await QueryAsync(db, DispatchesRowLevelSecuritySql)).Should().Equal(["true:false"], "enabled, not forced");
        var targetsPolicies = await QueryAsync(db, TargetPolicyTextsSql);
        targetsPolicies.Should().Contain(p => p.StartsWith("tenant_isolation_policy|PERMISSIVE|ALL|", StringComparison.Ordinal)
            && p.Contains("service_order_owned_by_or_granted_to_current_organization", StringComparison.Ordinal),
            "writing targets is restated on owner-or-grantee, so seeing an order through a dispatch grants no write");
        targetsPolicies.Should().Contain(p => p.StartsWith("dispatch_recipient_select_policy|PERMISSIVE|SELECT|", StringComparison.Ordinal)
            && p.Contains("service_order_dispatched_to_current_organization", StringComparison.Ordinal));
        (await QueryAsync(db, StoredDispatchRowsSql)).Should().Equal(["ord-1:2:1"], "no row was touched");

        // Down: every policy as the previous migration left it, the functions gone, the rows kept.
        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().Equal(
            "ServiceOrderTargets|tenant_isolation_policy|PERMISSIVE|ALL",
            "ServiceOrders|tenant_isolation_policy|PERMISSIVE|ALL");
        (await QueryAsync(db, TargetPolicySql)).Should().Equal([targetsPolicyBefore], "the targets policy is back to the text it had");
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().BeEmpty();
        (await QueryAsync(db, DispatchesRowLevelSecuritySql)).Should().Equal("false:false");
        (await QueryAsync(db, StoredDispatchRowsSql)).Should().Equal("ord-1:2:1");

        // Up again — to this migration, whose policies and functions are counted, then to the head
        // (XD01-333's migration adds one policy and two functions of its own).
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, DispatchRecipientPoliciesSql)).Should().HaveCount(8);
        (await QueryAsync(db, DispatchRecipientFunctionsSql)).Should().HaveCount(3);
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
}
