using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Applies the <b>real</b> <see cref="ServiceOrderDbContext"/> migration chain carried by
/// <c>GeoAssets.Server</c> to an empty PostgreSQL database and proves the XD01-304 migration
/// (<c>AddOrderTypeTargetPolicyAndAttributesSchema</c>: nullable <c>text</c> columns
/// <c>"OrderTypes"."TargetPolicyJson"</c> and <c>"OrderTypes"."AttributesSchemaJson"</c>) applies
/// on top of its predecessor, keeps existing rows, and rolls back cleanly.
///
/// <c>GeoAssets.Server</c> is one of the two hosts that carry a migration history for this context
/// (the shared-database one; <c>GeoAssets.FieldService.Server</c> carries the isolated-database
/// one and has its own twin of this suite). The same is proven for the XD01-306 pair
/// <c>AddServiceOrderTargets</c> / <c>AddServiceOrderTargetsRowLevelSecurity</c>. Each fact migrates its own, freshly-named database
/// inside <see cref="SecurityTestContainerFixture"/>'s container, so rolling a migration back here
/// can never disturb the schema the row-level-security facts share.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string MigrationName = "AddOrderTypeTargetPolicyAndAttributesSchema";

    // SqlQueryRaw<string> maps a scalar result set through a column named "Value".
    private const string NewOrderTypeColumnsSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable)::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'OrderTypes'
          AND column_name IN ('TargetPolicyJson', 'AttributesSchemaJson')
        ORDER BY column_name
        """;

    private const string OrderTypeRowLevelSecuritySql =
        """
        SELECT (relrowsecurity::text || ':' || (SELECT count(*) FROM pg_policies WHERE tablename = 'OrderTypes')::text) AS "Value"
        FROM pg_class
        WHERE oid = '"OrderTypes"'::regclass
        """;

    [Fact]
    public async Task MigrateAsync_OnEmptyPostgres_AppliesEveryMigrationInTheChain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        var chain = db.Database.GetMigrations().ToList();
        chain.Should().Contain(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        // In the shared database "__EFMigrationsHistory" also lists GeoIdentityDbContext's
        // migrations (applied by NewContextOnEmptyDatabaseAsync); only this context's own are compared.
        (await db.Database.GetAppliedMigrationsAsync()).Where(chain.Contains).Should().Equal(chain);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().Equal(
            "AttributesSchemaJson:text:YES", "TargetPolicyJson:text:YES");
    }

    [Fact]
    public async Task OrderTypeTargetPolicyMigration_AppliesOverExistingRows_ThenRollsBackAndReapplies()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var target = chain.Single(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        var previous = chain[chain.IndexOf(target) - 1];

        // The schema every deployed database has today, with one order type already stored.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().BeEmpty();
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description") VALUES ('inspection', 'Inspection', '')""");

        // Up.
        await migrator.MigrateAsync(target);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().Equal(
            "AttributesSchemaJson:text:YES", "TargetPolicyJson:text:YES");
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "OrderTypes" WHERE "TargetPolicyJson" IS NULL AND "AttributesSchemaJson" IS NULL"""))
            .Should().Equal("inspection");
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "OrderTypes" SET "TargetPolicyJson" = '{{"targetAssetTypeIds":["pole"]}}', "AttributesSchemaJson" = '{{"type":"object"}}' WHERE "Id" = 'inspection'""");

        // Order types are not tenant-scoped: the migration must not have put RLS on the table.
        (await QueryAsync(db, OrderTypeRowLevelSecuritySql)).Should().Equal("false:0");

        // Down.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(target);
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "OrderTypes" """)).Should().Equal("inspection");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, NewOrderTypeColumnsSql)).Should().HaveCount(2);
    }

    // ── XD01-306: "ServiceOrderTargets" and its row-level security ────────────

    private const string TargetsTableMigration = "AddServiceOrderTargets";
    private const string TargetsRowLevelSecurityMigration = "AddServiceOrderTargetsRowLevelSecurity";

    private const string TargetColumnsSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(character_maximum_length::text, '-') || ':' || coalesce(collation_name, '-'))::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'ServiceOrderTargets'
        ORDER BY ordinal_position
        """;

    private const string TargetIndexesSql =
        """
        SELECT indexdef::text AS "Value" FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'ServiceOrderTargets'
        ORDER BY indexname
        """;

    private const string TargetForeignKeysSql =
        """
        SELECT (conname || ' ' || pg_get_constraintdef(oid))::text AS "Value" FROM pg_constraint
        WHERE conrelid = '"ServiceOrderTargets"'::regclass AND contype = 'f'
        """;

    private const string TargetRowLevelSecuritySql =
        """
        SELECT (relrowsecurity::text || ':' || relforcerowsecurity::text || ':'
                || (SELECT count(*) FROM pg_policies WHERE schemaname = 'public' AND tablename = 'ServiceOrderTargets')::text) AS "Value"
        FROM pg_class
        WHERE oid = '"ServiceOrderTargets"'::regclass
        """;

    private const string TargetPolicySql =
        """
        SELECT (policyname || '|' || permissive || '|' || cmd || '|' || qual || '|' || with_check)::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'ServiceOrderTargets'
        """;

    private const string TargetsTableExistsSql =
        """SELECT (to_regclass('public."ServiceOrderTargets"') IS NOT NULL)::text AS "Value" """;

    [Fact]
    public async Task ServiceOrderTargetsMigration_CreatesTheTableColumnsIndexesAndCascadingForeignKey()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationId(db, TargetsTableMigration));

        (await QueryAsync(db, TargetColumnsSql)).Should().Equal(
            "Id:character varying:NO:36:C",
            "ServiceOrderId:character varying:NO:36:-",
            "FeatureId:character varying:NO:64:C",
            "AssetTypeId:character varying:NO:64:-",
            "Alias:character varying:YES:256:C",
            "ActivityIdsJson:text:NO:-:-",
            "Status:character varying:NO:64:C",
            "ConflictLevel:integer:NO:-:-",
            "Note:character varying:YES:2048:-",
            "Longitude:double precision:YES:-:-",
            "Latitude:double precision:YES:-:-",
            "AddedAt:timestamp with time zone:NO:-:-",
            "AddedBy:character varying:NO:256:-",
            "OrganizationId:uuid:NO:-:-",
            "Version:integer:NO:-:-",
            "ReleasedAt:timestamp with time zone:YES:-:-");
        (await QueryAsync(db, TargetIndexesSql)).Should().Equal(
            """CREATE INDEX "IX_ServiceOrderTargets_FeatureId" ON public."ServiceOrderTargets" USING btree ("FeatureId") WHERE ("ReleasedAt" IS NULL)""",
            """CREATE INDEX "IX_ServiceOrderTargets_OrganizationId" ON public."ServiceOrderTargets" USING btree ("OrganizationId")""",
            """CREATE UNIQUE INDEX "IX_ServiceOrderTargets_ServiceOrderId_FeatureId" ON public."ServiceOrderTargets" USING btree ("ServiceOrderId", "FeatureId")""",
            """CREATE INDEX "IX_ServiceOrderTargets_ServiceOrderId_Status" ON public."ServiceOrderTargets" USING btree ("ServiceOrderId", "Status")""",
            """CREATE UNIQUE INDEX "PK_ServiceOrderTargets" ON public."ServiceOrderTargets" USING btree ("Id")""");
        (await QueryAsync(db, TargetForeignKeysSql)).Should().Equal(
            """FK_ServiceOrderTargets_ServiceOrders_ServiceOrderId FOREIGN KEY ("ServiceOrderId") REFERENCES "ServiceOrders"("Id") ON DELETE CASCADE""");
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal(
            ["false:false:0"], "row-level security arrives with the next migration, one RLS migration per table as elsewhere in this history");
    }

    [Fact]
    public async Task ServiceOrderTargetsMigrations_ApplyOverExistingOrders_ThenRollBackStepByStepAndReapply()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var table = MigrationId(db, TargetsTableMigration);
        var rowLevelSecurity = MigrationId(db, TargetsRowLevelSecurityMigration);
        var previous = chain[chain.IndexOf(table) - 1];
        chain.IndexOf(rowLevelSecurity).Should().Be(chain.IndexOf(table) + 1);

        // The schema every deployed database has today, with an order already stored.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, TargetsTableExistsSql)).Should().Equal("false");
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
            VALUES ('ord-1', 'Order 1', '', 'inspection', 'Draft', 0, 'tester', '11111111-1111-1111-1111-111111111111', now(), '{{}}', '[]')
            """);

        // Up: table, then policy.
        await migrator.MigrateAsync(table);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
            VALUES ('tgt-1', 'ord-1', 'feat-1', 'pole', '[]', 'Pending', 0, now(), 'tester', '11111111-1111-1111-1111-111111111111', 0)
            """);
        await migrator.MigrateAsync(rowLevelSecurity);
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal(["true:false:1"], "enabled, not forced, exactly one policy");
        var policy = (await QueryAsync(db, TargetPolicySql)).Should().ContainSingle().Subject;
        policy.Should().StartWith("tenant_isolation_policy|PERMISSIVE|ALL|");
        policy.Should().Contain("""("OrganizationId" = (current_setting('app.current_organization_id'::text, true))::uuid)""");
        policy.Should().Contain("""FROM "ServiceOrders" o""").And.Contain("""(o."OrganizationId" = "ServiceOrderTargets"."OrganizationId")""");
        policy.Should().NotContain("OrganizationGrants", "grant visibility is inherited through the parent order, not restated");

        // Down, one step: the policy goes, the table and its rows stay.
        await migrator.MigrateAsync(table);
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal("false:false:0");
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("tgt-1");

        // Down, second step: the table goes, the order stays.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, TargetsTableExistsSql)).Should().Equal("false");
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain([table, rowLevelSecurity]);
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrders" """)).Should().Equal("ord-1");

        // Up again: back to this pair first, where the table has the one policy this pair gives
        // it (a later migration, XD01-316, adds a second), then on to the head of the chain.
        await migrator.MigrateAsync(rowLevelSecurity);
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal("true:false:1");
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeletingAnOrder_InTheMigratedSchema_DeletesItsTargets()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
            VALUES ('ord-1', 'Order 1', '', 'inspection', 'Draft', 0, 'tester', '11111111-1111-1111-1111-111111111111', now(), '{{}}', '[]');
            INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
            VALUES ('tgt-1', 'ord-1', 'feat-1', 'pole', '[]', 'Pending', 0, now(), 'tester', '11111111-1111-1111-1111-111111111111', 0);
            """);

        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-1'""");

        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().BeEmpty();
    }

    [Fact]
    public async Task Model_HasNoChangesTheMigrationChainDoesNotAlreadyContain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "a model change without a migration would leave this host's database behind the code");
    }

    private static string MigrationId(ServiceOrderDbContext db, string name) =>
        db.Database.GetMigrations().Single(id => id.EndsWith("_" + name, StringComparison.Ordinal));

    private static Task<List<string>> QueryAsync(ServiceOrderDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    /// <summary>
    /// A context on a database that holds only what this chain depends on from outside itself:
    /// the ServiceOrders RLS migrations reference "OrganizationGrants", which
    /// <see cref="GeoIdentityDbContext"/>'s own chain creates.
    /// </summary>
    private static async Task<ServiceOrderDbContext> NewContextOnEmptyDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Database = $"service_order_chain_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
        {
            await identity.Database.MigrateAsync();
        }

        return new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString));
    }
}
