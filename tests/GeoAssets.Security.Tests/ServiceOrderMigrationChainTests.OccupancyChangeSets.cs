using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-315 pair <c>AddServiceOrderOccupancyChangeSets</c> /
/// <c>AddServiceOrderOccupancyChangeSetsRowLevelSecurity</c> in the <c>GeoAssets.Server</c>
/// history: the table of staged occupancy changes as the migration really creates it, up, down
/// and up again. What the policy lets each tenant see and write is in
/// <see cref="OccupancyChangeSetRowLevelSecurityTests"/>.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string ChangeSetsTable = "ServiceOrderOccupancyChangeSets";
    private const string ChangeSetsTableMigration = "AddServiceOrderOccupancyChangeSets";
    private const string ChangeSetsRowLevelSecurityMigration = "AddServiceOrderOccupancyChangeSetsRowLevelSecurity";

    private const string ChangeSetsRowLevelSecuritySql =
        """
        SELECT (c.relrowsecurity::text || ':' || c.relforcerowsecurity::text || ':'
                || (SELECT count(*) FROM pg_policies p WHERE p.schemaname = 'public' AND p.tablename = c.relname)::text) AS "Value"
        FROM pg_class c
        WHERE c.relnamespace = 'public'::regnamespace AND c.relname = 'ServiceOrderOccupancyChangeSets'
        """;

    private const string ChangeSetsPolicySql =
        """
        SELECT (policyname || '|' || permissive || '|' || cmd || '|' || qual || '|' || with_check)::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'ServiceOrderOccupancyChangeSets'
        """;

    private const string ChangeSetsTableExistsSql =
        """SELECT (to_regclass('public."ServiceOrderOccupancyChangeSets"') IS NOT NULL)::text AS "Value" """;

    private const string ChangeSetStatusesSql =
        """SELECT ("TargetId" || ':' || "Status")::text AS "Value" FROM "ServiceOrderOccupancyChangeSets" ORDER BY "TargetId", "Status" """;

    private const string InsertOrderWithTwoTargetsSql =
        """
        INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
        VALUES ('ord-1', 'Order 1', '', 'inspection', 'Draft', 0, 'tester', '11111111-1111-1111-1111-111111111111', now(), '{{}}', '[]');
        INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
        VALUES ('tgt-1', 'ord-1', 'pole-1', 'pole', '[]', 'Pending', 0, now(), 'tester', '11111111-1111-1111-1111-111111111111', 0),
               ('tgt-2', 'ord-1', 'pole-2', 'pole', '[]', 'Pending', 0, now(), 'tester', '11111111-1111-1111-1111-111111111111', 0);
        """;

    private static string InsertChangeSetSql(string targetId, string status) =>
        $"""
        INSERT INTO "ServiceOrderOccupancyChangeSets"
            ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
        VALUES (gen_random_uuid(), 'ord-1', '{targetId}', 'pole-1', '[]', 0, '{status}', 'tester', now(), NULL, '11111111-1111-1111-1111-111111111111')
        """;

    [Fact]
    public async Task OccupancyChangeSetsMigration_CreatesTheTableWithItsColumnsIndexesAndCascadingForeignKey()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationId(db, ChangeSetsTableMigration));

        (await QueryAsync(db, OccupancyColumnsSql(ChangeSetsTable))).Should().Equal(
            "Id:uuid:NO:-:-:-",
            "ServiceOrderId:character varying:NO:36:-:-",
            "TargetId:character varying:NO:36:C:-",
            "PoleFeatureId:character varying:NO:64:C:-",
            "ChangesJson:text:NO:-:-:-",
            "BaselineVersion:integer:NO:-:-:-",
            "Status:character varying:NO:16:-:-",
            "StagedBy:character varying:NO:256:-:-",
            "StagedAt:timestamp with time zone:NO:-:-:-",
            "ClosedAt:timestamp with time zone:YES:-:-:-",
            "OrganizationId:uuid:NO:-:-:-");
        (await QueryAsync(db, OccupancyIndexesSql(ChangeSetsTable))).Should().Equal(
            """CREATE INDEX "IX_ServiceOrderOccupancyChangeSets_OrganizationId" ON public."ServiceOrderOccupancyChangeSets" USING btree ("OrganizationId")""",
            """CREATE INDEX "IX_ServiceOrderOccupancyChangeSets_ServiceOrderId_Status" ON public."ServiceOrderOccupancyChangeSets" USING btree ("ServiceOrderId", "Status")""",
            """CREATE INDEX "IX_ServiceOrderOccupancyChangeSets_TargetId" ON public."ServiceOrderOccupancyChangeSets" USING btree ("TargetId")""",
            """CREATE UNIQUE INDEX "IX_ServiceOrderOccupancyChangeSets_TargetId_Staged" ON public."ServiceOrderOccupancyChangeSets" USING btree ("TargetId") WHERE (("Status")::text = 'Staged'::text)""",
            """CREATE UNIQUE INDEX "PK_ServiceOrderOccupancyChangeSets" ON public."ServiceOrderOccupancyChangeSets" USING btree ("Id")""");
        (await QueryAsync(db, OccupancyConstraintsSql($"\"{ChangeSetsTable}\""))).Should().Equal(
            """FK_ServiceOrderOccupancyChangeSets_ServiceOrderTargets_TargetId FOREIGN KEY ("TargetId") REFERENCES "ServiceOrderTargets"("Id") ON DELETE CASCADE""");
        (await QueryAsync(db, ChangeSetsRowLevelSecuritySql)).Should().Equal(
            ["false:false:0"], "row-level security arrives with the next migration, one RLS migration per table as elsewhere in this history");
    }

    [Fact]
    public async Task OccupancyChangeSetsMigrations_ApplyOverExistingTargets_ThenRollBackStepByStepAndReapply()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var table = MigrationId(db, ChangeSetsTableMigration);
        var rowLevelSecurity = MigrationId(db, ChangeSetsRowLevelSecurityMigration);
        var previous = chain[chain.IndexOf(table) - 1];
        chain.IndexOf(rowLevelSecurity).Should().Be(chain.IndexOf(table) + 1);
        chain.IndexOf(table).Should().BeGreaterThan(chain.IndexOf(MigrationId(db, OccupancyRowLevelSecurityMigration)), "staged changes come after the occupancy they propose to change");

        // The schema every deployed database has today, with an order and its targets stored.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, ChangeSetsTableExistsSql)).Should().Equal("false");
        await db.Database.ExecuteSqlRawAsync(InsertOrderWithTwoTargetsSql);

        // Up: table, then policy.
        await migrator.MigrateAsync(table);
        await db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-1", "Staged"));
        await migrator.MigrateAsync(rowLevelSecurity);
        (await QueryAsync(db, ChangeSetsRowLevelSecuritySql)).Should().Equal(["true:false:1"], "enabled, not forced, exactly one policy");
        var policy = (await QueryAsync(db, ChangeSetsPolicySql)).Should().ContainSingle().Subject;
        const string OwnerOnly = """("OrganizationId" = (current_setting('app.current_organization_id'::text, true))::uuid)""";
        policy.Should().StartWith($"tenant_isolation_policy|PERMISSIVE|ALL|{OwnerOnly}|", "reading is the pole owner's alone: no grant clause, no parent-order clause");
        var withCheck = policy[(policy.LastIndexOf('|') + 1)..];
        withCheck.Should().Contain(OwnerOnly)
            .And.Contain("""FROM "ServiceOrderTargets" t""")
            .And.Contain("""((t."Id")::text = ("ServiceOrderOccupancyChangeSets"."TargetId")::text)""")
            .And.Contain("""((t."ServiceOrderId")::text = ("ServiceOrderOccupancyChangeSets"."ServiceOrderId")::text)""")
            .And.Contain("""(t."OrganizationId" = "ServiceOrderOccupancyChangeSets"."OrganizationId")""");
        policy.Should().NotContain("OrganizationGrants");

        // Down, one step: the policy goes, the table and its rows stay.
        await migrator.MigrateAsync(table);
        (await QueryAsync(db, ChangeSetsRowLevelSecuritySql)).Should().Equal("false:false:0");
        (await QueryAsync(db, ChangeSetStatusesSql)).Should().Equal("tgt-1:Staged");

        // Down, second step: the table goes; the order, its targets and the occupancy tables stay.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, ChangeSetsTableExistsSql)).Should().Equal("false");
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain([table, rowLevelSecurity]);
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" ORDER BY "Id" """)).Should().Equal("tgt-1", "tgt-2");
        (await QueryAsync(db, OccupancyTablesExistSql)).Should().Equal("true:true");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, ChangeSetsRowLevelSecuritySql)).Should().Equal("true:false:1");
    }

    [Fact]
    public async Task OccupancyChangeSets_InTheMigratedSchema_AllowOneStagedSetPerTarget_AndGoWithTheirTargetAndTheirOrder()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(InsertOrderWithTwoTargetsSql);
        await db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-1", "Staged"));
        await db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-1", "Discarded"));
        await db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-1", "Discarded"));
        await db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-1", "Committed"));
        await db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-2", "Staged"));
        var secondStaged = () => db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-1", "Staged"));
        var withoutATarget = () => db.Database.ExecuteSqlRawAsync(InsertChangeSetSql("tgt-missing", "Staged"));

        (await secondStaged.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        (await withoutATarget.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        (await QueryAsync(db, ChangeSetStatusesSql)).Should().Equal(
            "tgt-1:Committed", "tgt-1:Discarded", "tgt-1:Discarded", "tgt-1:Staged", "tgt-2:Staged");

        // Removing a target removes its sets, in every status; deleting the order removes the rest.
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "ServiceOrderTargets" WHERE "Id" = 'tgt-1'""");
        (await QueryAsync(db, ChangeSetStatusesSql)).Should().Equal("tgt-2:Staged");
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-1'""");
        (await QueryAsync(db, ChangeSetStatusesSql)).Should().BeEmpty();
    }
}
