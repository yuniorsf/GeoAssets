using FluentAssertions;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Migrations;

/// <summary>
/// The migration chains this host owns (XD01-382, AC4), run on Postgres: the host's <c>ServiceOrderDbContext</c> chain
/// (<c>InitialNeutralOperatorIsolation</c>, with hand-written SQL for what EF cannot express) applies to an empty database, then
/// <c>NeutralOperatorDbContext</c>'s on top of it; neither model drifts from its chain; the isolation SQL is in force.
/// </summary>
public sealed class NeutralOperatorMigrationChainTests
{
    private static readonly string[] KernelTablesWithRowLevelSecurity = ["OrderActionLogs", "OrderDispatches", "ServiceOrderTargets", "ServiceOrders"];

    private static readonly string[] VerticalTables = ["field_pole_occupancies", "field_pole_occupancy_entries", "ServiceOrderOccupancyChangeSets", "occupancy_apply_saga_poles"];

    [Fact]
    public async Task ServiceOrderChain_OnAnEmptyDatabase_AppliesEveryMigration_AndTheVerticalsChainApplies_OnTopOfIt()
    {
        var connectionString = await NeutralOperatorDatabase.NewNameOnEmptyServerAsync();
        await using var serviceOrders = NeutralOperatorDatabase.ServiceOrderContext(connectionString);
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);

        await serviceOrders.Database.MigrateAsync();
        (await serviceOrders.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await serviceOrders.Database.GetAppliedMigrationsAsync()).Should().ContainSingle().Which.Should().EndWith("_InitialNeutralOperatorIsolation");
        (await vertical.Database.GetPendingMigrationsAsync()).Should().NotBeEmpty("the vertical's chain waits for the kernel tables it adopts");

        await vertical.Database.MigrateAsync();

        (await vertical.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await vertical.Database.GetAppliedMigrationsAsync()).Should().HaveCount(3).And.SatisfyRespectively(
            baseline => baseline.Should().EndWith("_AdoptOccupancyTables"),
            saga => saga.Should().EndWith("_AddOccupancyApplySagas"),
            unrestored => unrestored.Should().EndWith("_AddOccupancyApplySagaUnrestoredFeatureIds"));
        (await DatabaseProbe.ValuesAsync(connectionString, """
            SELECT (table_schema || '.' || table_name)::text AS "Value" FROM information_schema.tables
            WHERE table_schema IN ('public', 'neutral_operator') AND table_type = 'BASE TABLE'
            """)).Should().BeEquivalentTo(
            "neutral_operator.__EFMigrationsHistory_neutral_operator",
            "neutral_operator.field_pole_occupancies",
            "neutral_operator.field_pole_occupancy_entries",
            "neutral_operator.ServiceOrderOccupancyChangeSets",
            "neutral_operator.occupancy_apply_sagas",
            "neutral_operator.occupancy_apply_saga_poles",
            "public.__EFMigrationsHistory",
            "public.OrderActionLogs",
            "public.OrderActionPermissions",
            "public.OrderCreationPolicies",
            "public.OrderDispatches",
            "public.OrderTypeRecipientActions",
            "public.OrderTypes",
            "public.OrderTypeStates",
            "public.OrderTypeTransitions",
            "public.OrganizationGrantMirror",
            "public.OrganizationGrantSyncCursor",
            "public.ServiceOrders",
            "public.ServiceOrderTargets",
            "public.WorkflowInboxProcessedMessages",
            "public.WorkflowOutboxMessages");
    }

    [Fact]
    public async Task VerticalChain_WithoutTheServiceOrderChainUnderIt_Refuses_AndCreatesNothing()
    {
        var connectionString = await NeutralOperatorDatabase.NewNameOnEmptyServerAsync();
        await using var vertical = NeutralOperatorDatabase.VerticalContext(connectionString);

        var act = async () => await vertical.Database.MigrateAsync();

        await act.Should().ThrowAsync<Exception>("the vertical's baseline adopts tables the host's own chain creates");
        (await DatabaseProbe.ValuesAsync(connectionString, """
            SELECT table_name::text AS "Value" FROM information_schema.tables WHERE table_schema = 'neutral_operator' AND table_name NOT LIKE '__EF%'
            """)).Should().BeEmpty();
    }

    [Fact]
    public async Task ServiceOrderModel_HasNoChangesTheHostsChainDoesNotAlreadyContain()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        await using var db = NeutralOperatorDatabase.ServiceOrderContext(connectionString);

        db.Database.HasPendingModelChanges().Should().BeFalse("a model change without a migration would leave this host's database behind the code");
    }

    [Fact]
    public async Task NeutralOperatorModel_HasNoChangesItsChainDoesNotAlreadyContain()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        await using var db = NeutralOperatorDatabase.VerticalContext(connectionString);

        db.Database.HasPendingModelChanges().Should().BeFalse("a model change without a migration would leave this host's database behind the code");
    }

    [Fact]
    public async Task RowLevelSecurity_IsEnabledNotForced_OnEveryOrderTable_WithTheTenantIsolationPolicy()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();

        (await DatabaseProbe.ValuesAsync(connectionString, """
            SELECT (c.relname || ':' || c.relrowsecurity::text || ':' || c.relforcerowsecurity::text)::text AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname IN ('ServiceOrders', 'ServiceOrderTargets', 'OrderActionLogs', 'OrderDispatches')
            """)).Should().BeEquivalentTo(KernelTablesWithRowLevelSecurity.Select(t => $"{t}:true:false"));
        (await DatabaseProbe.ValuesAsync(connectionString, """
            SELECT (tablename || '|' || policyname || '|' || permissive || '|' || cmd)::text AS "Value" FROM pg_policies WHERE schemaname = 'public'
            """)).Should().BeEquivalentTo(
            "OrderActionLogs|dispatch_recipient_insert_policy|PERMISSIVE|INSERT",
            "OrderActionLogs|dispatch_recipient_select_policy|PERMISSIVE|SELECT",
            "OrderActionLogs|tenant_isolation_policy|PERMISSIVE|ALL",
            "OrderDispatches|dispatch_recipient_select_policy|PERMISSIVE|SELECT",
            "OrderDispatches|tenant_isolation_policy|PERMISSIVE|ALL",
            "ServiceOrders|dispatch_recipient_action_update_policy|PERMISSIVE|UPDATE",
            "ServiceOrders|dispatch_recipient_select_policy|PERMISSIVE|SELECT",
            "ServiceOrders|dispatch_recipient_update_policy|PERMISSIVE|UPDATE",
            "ServiceOrders|organization_is_fixed_policy|RESTRICTIVE|UPDATE",
            "ServiceOrders|tenant_isolation_policy|PERMISSIVE|ALL",
            "ServiceOrderTargets|dispatch_recipient_select_policy|PERMISSIVE|SELECT",
            "ServiceOrderTargets|dispatch_recipient_update_policy|PERMISSIVE|UPDATE",
            "ServiceOrderTargets|ledger_vertical_isolation_policy|RESTRICTIVE|ALL",
            "ServiceOrderTargets|ledger_vertical_tenant_policy|PERMISSIVE|ALL",
            "ServiceOrderTargets|tenant_isolation_policy|PERMISSIVE|ALL");
    }

    [Fact]
    public async Task RowLevelSecurity_IsEnabledOnTheVerticalsTables_WithTheirTenantIsolationPolicy()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();

        (await DatabaseProbe.ValuesAsync(connectionString, """
            SELECT (c.relname || ':' || c.relrowsecurity::text || ':' || c.relforcerowsecurity::text)::text AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'neutral_operator' AND c.relkind = 'r' AND c.relname NOT LIKE '__EF%'
            """)).Should().BeEquivalentTo(
            [.. VerticalTables.Order(StringComparer.Ordinal).Select(t => $"{t}:true:false"), "occupancy_apply_sagas:false:false"],
            "the saga table holds ids, states and instants only — no occupancy — so, like the outbox, it has no policy");
        (await DatabaseProbe.ValuesAsync(connectionString, """
            SELECT (tablename || '|' || policyname)::text AS "Value" FROM pg_policies WHERE schemaname = 'neutral_operator'
            """)).Should().BeEquivalentTo(VerticalTables.Order(StringComparer.Ordinal).Select(t => $"{t}|tenant_isolation_policy"));
    }

    [Fact]
    public async Task TheRowVersionTrigger_StampsAFreshVersion_OnEveryInsertAndUpdate()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        await using (var owner = await KernelTables.OpenAsOwnerAsync(connectionString))
            await KernelTables.InsertOrderAsync(owner, "ord-rv", TestIdentities.OwnerOrganization);
        var inserted = await DatabaseProbe.ValueAsync(connectionString, """SELECT encode("RowVersion", 'hex') AS "Value" FROM "ServiceOrders" WHERE "Id" = 'ord-rv'""");

        await using (var db = NeutralOperatorDatabase.ServiceOrderContext(connectionString))
            await db.Database.ExecuteSqlRawAsync("""UPDATE "ServiceOrders" SET "Title" = 'renamed' WHERE "Id" = 'ord-rv'""");
        var updated = await DatabaseProbe.ValueAsync(connectionString, """SELECT encode("RowVersion", 'hex') AS "Value" FROM "ServiceOrders" WHERE "Id" = 'ord-rv'""");

        inserted.Should().HaveLength(32, "the trigger replaces the supplied one-byte version with a UUID").And.NotBe("01");
        updated.Should().HaveLength(32).And.NotBe(inserted);
    }

    [Fact]
    public async Task TheKernelOrderIdTrigger_FillsKernelOrderIdForKernelTargetsOnly()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();
        await using (var owner = await KernelTables.OpenAsOwnerAsync(connectionString))
        {
            await KernelTables.InsertOrderAsync(owner, "ord-k", TestIdentities.OwnerOrganization);
            await KernelTables.InsertTargetAsync(owner, "tgt-k", "ord-k", TestIdentities.OwnerOrganization);
        }

        (await DatabaseProbe.ValueAsync(connectionString, """SELECT coalesce("KernelOrderId", '-') AS "Value" FROM "ServiceOrderTargets" WHERE "Id" = 'tgt-k'""")).Should().Be("ord-k");
    }

    [Fact]
    public async Task TheChain_SeedsTheSharedUseNoticesRecipientActions_AndNothingElse()
    {
        var connectionString = await NeutralOperatorDatabase.CreateMigratedAsync();

        (await DatabaseProbe.ValuesAsync(connectionString, """
            SELECT (a."OrderTypeId" || '|' || coalesce(a."Action"::text, '-') || '|' || coalesce(a."ActionCode", '-') || '|' || coalesce(a."FromStateKey", '-') || '|' || coalesce(a."ToStateKey", '-'))::text AS "Value"
            FROM "OrderTypeRecipientActions" a
            """)).Should().BeEquivalentTo(
            "shared-use-occupant-notice|4|Accept|-|-",
            "shared-use-occupant-notice|4|Accept|Pending|InProgress",
            "shared-use-occupant-notice|9|Annotate|-|-");
        (await DatabaseProbe.ValueAsync(connectionString, """SELECT count(*)::text AS "Value" FROM "OrderTypes" """)).Should().Be("0");
    }
}
