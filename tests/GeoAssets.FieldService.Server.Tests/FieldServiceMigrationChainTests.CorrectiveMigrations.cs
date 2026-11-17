using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The three XD01-323 migrations as migrations: <c>AddServiceOrderRowVersionTrigger</c>,
/// <c>AddIsolatedHistoryRowLevelSecurity</c> and <c>AddOrderChildTablesRowLevelSecurity</c>
/// applied on top of a database that is already at the previous migration and already holds two
/// tenants' rows, rolled back, and applied again — with the state they correct (no policy, no
/// row version) asserted first, so this class also records what the database was like without them.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string LastMigrationBeforeTheCorrection = "AddPoleOccupanciesRowLevelSecurity";
    private const string RowVersionTriggerMigration = "AddServiceOrderRowVersionTrigger";
    private const string IsolatedHistoryRowLevelSecurityMigration = "AddIsolatedHistoryRowLevelSecurity";
    private const string OrderChildTablesRowLevelSecurityMigration = "AddOrderChildTablesRowLevelSecurity";

    private const string OwnOrganizationOnly = """("OrganizationId" = (current_setting('app.current_organization_id'::text, true))::uuid)""";

    private static readonly string[] TablesTheServerHistoryProtects =
        ["ContractorUsLedgerEntries", "ServiceOrders", "UsCatalogItems", "field_icerp_paving_measurements", "field_manhole_audits"];

    private static readonly string[] TablesNoHistoryProtected =
        ["OrderActionLogs", "OrderDispatches", "field_contractor_us_postings"];

    /// <summary>name:enabled:forced:policies for the eight tables the correction covers, by name.</summary>
    private const string CorrectedTablesRowLevelSecuritySql =
        """
        SELECT (c.relname || ':' || c.relrowsecurity::text || ':' || c.relforcerowsecurity::text || ':'
                || (SELECT count(*) FROM pg_policies p WHERE p.schemaname = 'public' AND p.tablename = c.relname)::text) AS "Value"
        FROM pg_class c
        WHERE c.relnamespace = 'public'::regnamespace
          AND c.relname IN ('ServiceOrders', 'ContractorUsLedgerEntries', 'UsCatalogItems', 'field_manhole_audits',
                            'field_icerp_paving_measurements', 'OrderActionLogs', 'OrderDispatches', 'field_contractor_us_postings')
        ORDER BY c.relname COLLATE "C"
        """;

    private const string RowVersionTriggerSql =
        """
        SELECT (t.tgname || ' ' || pg_get_triggerdef(t.oid))::text AS "Value" FROM pg_trigger t
        WHERE t.tgrelid = '"ServiceOrders"'::regclass AND NOT t.tgisinternal
        """;

    private const string RowVersionFunctionExistsSql =
        """SELECT (to_regproc('touch_service_orders_row_version') IS NOT NULL)::text AS "Value" """;

    /// <summary>How many rows of each of the eight tables the session sees, as table:count.</summary>
    private const string VisibleRowCountsSql =
        """
        SELECT v."Value" FROM (
            SELECT ('ContractorUsLedgerEntries:' || count(*))::text AS "Value" FROM "ContractorUsLedgerEntries"
            UNION ALL SELECT 'OrderActionLogs:' || count(*) FROM "OrderActionLogs"
            UNION ALL SELECT 'OrderDispatches:' || count(*) FROM "OrderDispatches"
            UNION ALL SELECT 'ServiceOrders:' || count(*) FROM "ServiceOrders"
            UNION ALL SELECT 'UsCatalogItems:' || count(*) FROM "UsCatalogItems"
            UNION ALL SELECT 'field_contractor_us_postings:' || count(*) FROM field_contractor_us_postings
            UNION ALL SELECT 'field_icerp_paving_measurements:' || count(*) FROM field_icerp_paving_measurements
            UNION ALL SELECT 'field_manhole_audits:' || count(*) FROM field_manhole_audits
        ) v
        ORDER BY v."Value" COLLATE "C"
        """;

    private static readonly string[] CorrectedTables = [.. TablesTheServerHistoryProtects.Concat(TablesNoHistoryProtected).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task MigrateAsync_OnEmptyPostgres_EndsWithTheThreeCorrectiveMigrationsAndTheirPolicies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        // Up to the last of the three: what follows describes the policies as they leave them.
        // A later migration (XD01-316, dispatch recipients) adds policies of its own to
        // "ServiceOrders" and "OrderDispatches"; the chain is taken to its head at the end.
        await db.GetService<IMigrator>().MigrateAsync(MigrationId(db, OrderChildTablesRowLevelSecurityMigration));

        var chain = db.Database.GetMigrations().ToList();
        chain.Skip(chain.IndexOf(MigrationId(db, LastMigrationBeforeTheCorrection)) + 1).Take(3).Should().Equal(
            MigrationId(db, RowVersionTriggerMigration),
            MigrationId(db, IsolatedHistoryRowLevelSecurityMigration),
            MigrationId(db, OrderChildTablesRowLevelSecurityMigration));
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(
            CorrectedTables.Select(table => $"{table}:true:false:1"), "enabled, not forced (as in the shared database), exactly one policy each");
        (await QueryAsync(db, RowVersionTriggerSql)).Should().Equal(
            """service_orders_row_version_touch CREATE TRIGGER service_orders_row_version_touch BEFORE INSERT OR UPDATE ON public."ServiceOrders" FOR EACH ROW EXECUTE FUNCTION touch_service_orders_row_version()""");

        // The equality-only policies, as PostgreSQL stores them.
        var policies = (await QueryAsync(db,
            """
            SELECT (tablename || '|' || policyname || '|' || permissive || '|' || cmd || '|' || qual || '|' || with_check)::text AS "Value"
            FROM pg_policies WHERE schemaname = 'public'
            """)).ToDictionary(line => line[..line.IndexOf('|', StringComparison.Ordinal)], StringComparer.Ordinal);
        foreach (var table in new[] { "ContractorUsLedgerEntries", "UsCatalogItems", "field_manhole_audits", "field_icerp_paving_measurements" })
            policies[table].Should().Be($"{table}|tenant_isolation_policy|PERMISSIVE|ALL|{OwnOrganizationOnly}|{OwnOrganizationOnly}");
        var contractorOnly = OwnOrganizationOnly.Replace("\"OrganizationId\"", "\"ContractorOrganizationId\"", StringComparison.Ordinal);
        policies["field_contractor_us_postings"].Should().Be(
            $"field_contractor_us_postings|tenant_isolation_policy|PERMISSIVE|ALL|{contractorOnly}|{contractorOnly}");

        // The order policy reads the mirror, with the same expression for reads and writes.
        var orderPolicy = policies["ServiceOrders"].Split('|');
        orderPolicy[..4].Should().Equal("ServiceOrders", "tenant_isolation_policy", "PERMISSIVE", "ALL");
        orderPolicy[4].Should().Contain("\"OrganizationGrantMirror\"").And.Contain("\"ScopeId\"").And.Contain("'scopeId'");
        orderPolicy[5].Should().Be(orderPolicy[4]);

        await db.Database.MigrateAsync();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(chain);
    }

    [Fact]
    public async Task CorrectiveMigrations_ApplyOverTwoTenantsExistingRows_ThenRollBackStepByStepAndReapply()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var previous = MigrationId(db, LastMigrationBeforeTheCorrection);
        var trigger = MigrationId(db, RowVersionTriggerMigration);
        var isolatedHistory = MigrationId(db, IsolatedHistoryRowLevelSecurityMigration);
        var orderChildren = MigrationId(db, OrderChildTablesRowLevelSecurityMigration);
        var unprotected = CorrectedTables.Select(table => $"{table}:false:false:0").ToList();
        var twoOfEach = CorrectedTables.Select(table => $"{table}:2").ToList();
        var oneOfEach = CorrectedTables.Select(table => $"{table}:1").ToList();

        // The database as it is deployed today, holding one row of every table for each of two tenants.
        await migrator.MigrateAsync(previous);
        await using (var owner = await OpenAsync(db))
        {
            await SeedOneRowOfEveryCorrectedTableAsync(owner, "ord-a", OwnerOrganization);
            await SeedOneRowOfEveryCorrectedTableAsync(owner, "ord-b", StrangerOrganization);
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        await SetOrganizationAsync(scoped, StrangerOrganization);

        // What XD01-323 corrects: no policy on any of the eight tables, so a non-owner role acting
        // for tenant B reads tenant A's rows as well; and nothing fills "RowVersion", so EF cannot
        // create an order at all.
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(unprotected);
        (await ReadStringsAsync(scoped, VisibleRowCountsSql)).Should().Equal(twoOfEach, "without a policy both tenants' rows are visible");
        (await QueryAsync(db, RowVersionTriggerSql)).Should().BeEmpty();
        (await QueryAsync(db, RowVersionFunctionExistsSql)).Should().Equal("false");
        var connectionString = db.Database.GetConnectionString()!;
        var createThroughEF = () => AddOrderThroughEFAsync(connectionString, "ord-ef-before");
        (await createThroughEF.Should().ThrowAsync<DbUpdateException>()).Which.InnerException
            .Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.NotNullViolation);

        // Up, step by step.
        await migrator.MigrateAsync(trigger);
        (await QueryAsync(db, RowVersionTriggerSql)).Should().ContainSingle().Which.Should().StartWith("service_orders_row_version_touch ");
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(unprotected, "the trigger migration adds no policy");
        (await QueryAsync(db, """SELECT ("Id" || ':' || encode("RowVersion", 'hex'))::text AS "Value" FROM "ServiceOrders" ORDER BY "Id" """))
            .Should().Equal(["ord-a:01", "ord-b:01"], "stored orders keep their value until they are next updated");

        await migrator.MigrateAsync(isolatedHistory);
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(CorrectedTables.Select(table =>
            TablesTheServerHistoryProtects.Contains(table, StringComparer.Ordinal) ? $"{table}:true:false:1" : $"{table}:false:false:0"));

        await migrator.MigrateAsync(orderChildren);
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(CorrectedTables.Select(table => $"{table}:true:false:1"));
        // Nothing of this correction is left to apply: what is still pending is exactly what
        // later tickets added to this history after it (none when it was written; XD01-315's
        // staged-occupancy pair since) — applied by the final "up again" step below.
        var chain = db.Database.GetMigrations().ToList();
        (await db.Database.GetPendingMigrationsAsync()).Should().Equal(chain.Skip(chain.IndexOf(orderChildren) + 1));

        // Every row survived, and each tenant now reads its own only.
        (await QueryAsync(db, VisibleRowCountsSql)).Should().Equal(twoOfEach);
        (await ReadStringsAsync(scoped, VisibleRowCountsSql)).Should().Equal(oneOfEach);
        await SetOrganizationAsync(scoped, OwnerOrganization);
        (await ReadStringsAsync(scoped, VisibleRowCountsSql)).Should().Equal(oneOfEach);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal("ord-a");
        await SetOrganizationAsync(scoped, GranteeOrganization);
        (await ReadStringsAsync(scoped, VisibleRowCountsSql)).Should().Equal(CorrectedTables.Select(table => $"{table}:0"));
        await SetOrganizationAsync(scoped, StrangerOrganization);

        // Down, step by step: each migration removes exactly what it added, and no row.
        await migrator.MigrateAsync(isolatedHistory);
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(CorrectedTables.Select(table =>
            TablesTheServerHistoryProtects.Contains(table, StringComparer.Ordinal) ? $"{table}:true:false:1" : $"{table}:false:false:0"));
        await migrator.MigrateAsync(trigger);
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(unprotected);
        (await QueryAsync(db, RowVersionTriggerSql)).Should().ContainSingle();
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, RowVersionTriggerSql)).Should().BeEmpty();
        (await QueryAsync(db, RowVersionFunctionExistsSql)).Should().Equal("false");
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain([trigger, isolatedHistory, orderChildren]).And.Contain(previous);
        (await ReadStringsAsync(scoped, VisibleRowCountsSql)).Should().Equal(twoOfEach, "rolled back, the database is as exposed as before, with every row still there");
        // The policies that were there before the correction are untouched by its rollback.
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal("true:false:1");
        (await QueryAsync(db, OccupancyRowLevelSecuritySql)).Should().Equal(
            "field_pole_occupancies:true:false:1", "field_pole_occupancy_entries:true:false:1");

        // Up again: back to the correction first, where each table has the one policy it gives
        // it (a later migration, XD01-316, adds more to two of them), then on to the head.
        await migrator.MigrateAsync(orderChildren);
        (await QueryAsync(db, CorrectedTablesRowLevelSecuritySql)).Should().Equal(CorrectedTables.Select(table => $"{table}:true:false:1"));
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await ReadStringsAsync(scoped, VisibleRowCountsSql)).Should().Equal(oneOfEach);
        await AddOrderThroughEFAsync(connectionString, "ord-ef-after");
        (await QueryAsync(db, """SELECT length("RowVersion")::text AS "Value" FROM "ServiceOrders" WHERE "Id" = 'ord-ef-after'""")).Should().Equal("16");
    }

    /// <summary>
    /// The same assertions GeoAssets.Security.Tests runs against the shared-database history
    /// (<c>ServiceOrderRowVersionParityTests</c>): see <see cref="ServiceOrderRowVersionContract"/>.
    /// </summary>
    [Fact]
    public async Task ServiceOrderRowVersion_OnTheFieldServiceChain_GuardsConcurrentWritersAsOnTheSharedDatabase()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        var options = BuildOptions(db.Database.GetConnectionString()!);

        await ServiceOrderRowVersionContract.AssertGuardsConcurrentWritersAsync(() => new ServiceOrderDbContext(options));
    }

    private static async Task AddOrderThroughEFAsync(string connectionString, string id)
    {
        await using var context = new ServiceOrderDbContext(BuildOptions(connectionString));
        await new EFServiceOrderRepository(context).AddAsync(new ServiceOrder
        {
            Id = id,
            Title = id,
            CreatedBy = "tester",
            OrderTypeId = "inspection",
            CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        });
    }

    private static DbContextOptions<ServiceOrderDbContext> BuildOptions(string connectionString) =>
        new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssemblyName))
            .Options;

    /// <summary>An order of <paramref name="organizationId"/> and one row of each other corrected table belonging to it.</summary>
    private static async Task SeedOneRowOfEveryCorrectedTableAsync(NpgsqlConnection owner, string orderId, Guid organizationId)
    {
        await InsertOrderAsync(owner, orderId, organizationId);
        foreach (var table in EqualityTables.Values)
            await InsertTenantRowAsync(owner, table, Guid.NewGuid(), organizationId, orderId);
        foreach (var insertSql in OrderChildInserts.Values)
            await ExecuteAsync(owner, insertSql, ("orderId", orderId));
    }
}
