using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-314 pair <c>AddPoleOccupancies</c> / <c>AddPoleOccupanciesRowLevelSecurity</c> in the
/// <c>GeoAssets.Server</c> history: the two pole occupancy tables as the migration really
/// creates them, up, down and up again. What the policies let each tenant see is in
/// <see cref="PoleOccupancyRowLevelSecurityTests"/>.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string OccupancyTablesMigration = "AddPoleOccupancies";
    private const string OccupancyRowLevelSecurityMigration = "AddPoleOccupanciesRowLevelSecurity";
    private const string OccupancyOwner = "d1d1d1d1-0000-0000-0000-000000000001";

    private const string OccupancyRowLevelSecuritySql =
        """
        SELECT (c.relname || ':' || c.relrowsecurity::text || ':' || c.relforcerowsecurity::text || ':'
                || (SELECT count(*) FROM pg_policies p WHERE p.schemaname = 'public' AND p.tablename = c.relname)::text) AS "Value"
        FROM pg_class c
        WHERE c.relnamespace = 'public'::regnamespace AND c.relname IN ('field_pole_occupancies', 'field_pole_occupancy_entries')
        ORDER BY c.relname
        """;

    private const string OccupancyPoliciesSql =
        """
        SELECT (tablename || '|' || policyname || '|' || permissive || '|' || cmd || '|' || qual || '|' || with_check)::text AS "Value"
        FROM pg_policies
        WHERE schemaname = 'public' AND tablename IN ('field_pole_occupancies', 'field_pole_occupancy_entries')
        ORDER BY tablename
        """;

    private const string OccupancyTablesExistSql =
        """SELECT ((to_regclass('public.field_pole_occupancies') IS NOT NULL)::text || ':' || (to_regclass('public.field_pole_occupancy_entries') IS NOT NULL)::text) AS "Value" """;

    private const string OccupancyEntryCountSql =
        """SELECT count(*)::text AS "Value" FROM field_pole_occupancy_entries """;

    private const string InsertOccupancyPoleSql =
        """
        INSERT INTO field_pole_occupancies ("Id", "PoleFeatureId", "OrganizationId", "Version", "UpdatedAt")
        VALUES ('a0000000-0000-0000-0000-000000000001', 'pole-1', 'd1d1d1d1-0000-0000-0000-000000000001', 1, now())
        """;

    private static string OccupancyColumnsSql(string table) =>
        $"""
        SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(character_maximum_length::text, '-') || ':' || coalesce(collation_name, '-')
                || ':' || CASE WHEN data_type = 'numeric' THEN numeric_precision::text || ',' || numeric_scale::text ELSE '-' END)::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = '{table}'
        ORDER BY ordinal_position
        """;

    private static string OccupancyIndexesSql(string table) =>
        $"""
        SELECT indexdef::text AS "Value" FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = '{table}'
        ORDER BY indexname COLLATE "C"
        """;

    private static string OccupancyConstraintsSql(string table) =>
        $"""
        SELECT (conname || ' ' || pg_get_constraintdef(oid))::text AS "Value" FROM pg_constraint
        WHERE conrelid = 'public.{table}'::regclass AND contype IN ('f', 'c')
        ORDER BY conname COLLATE "C"
        """;

    /// <summary>One entry of pole <c>a0000000-…-01</c>; a null <paramref name="slotIndex"/> is an entry beyond the regulated slots.</summary>
    private static string InsertOccupancyEntrySql(int? slotIndex, string organizationId = OccupancyOwner) =>
        $"""
        INSERT INTO field_pole_occupancy_entries ("Id", "PoleOccupancyId", "SlotIndex", "Status", "CableDiameterMm", "CableLoadDaN", "OrganizationId")
        VALUES (gen_random_uuid(), 'a0000000-0000-0000-0000-000000000001', {(slotIndex is null ? "NULL" : slotIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}, 'ClandestineUnauthorized', 12.345, 0, '{organizationId}')
        """;

    [Fact]
    public async Task PoleOccupanciesMigration_CreatesBothTablesWithTheirColumnsIndexesCheckAndCascadingForeignKey()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationId(db, OccupancyTablesMigration));

        (await QueryAsync(db, OccupancyColumnsSql("field_pole_occupancies"))).Should().Equal(
            "Id:uuid:NO:-:-:-",
            "PoleFeatureId:character varying:NO:64:C:-",
            "OrganizationId:uuid:NO:-:-:-",
            "Version:integer:NO:-:-:-",
            "UpdatedAt:timestamp with time zone:NO:-:-:-");
        (await QueryAsync(db, OccupancyColumnsSql("field_pole_occupancy_entries"))).Should().Equal(
            "Id:uuid:NO:-:-:-",
            "PoleOccupancyId:uuid:NO:-:-:-",
            "SlotIndex:integer:YES:-:-:-",
            "Status:character varying:NO:32:-:-",
            "OccupantOrganizationId:uuid:YES:-:-:-",
            "OperatorCnpj:character varying:YES:18:-:-",
            "AnatelLicenseNumber:character varying:YES:64:-:-",
            "CableDiameterMm:numeric:NO:-:-:8,3",
            "CableLoadDaN:numeric:NO:-:-:12,3",
            "AttachedCableFeatureId:character varying:YES:64:-:-",
            "OrganizationId:uuid:NO:-:-:-");
        (await QueryAsync(db, OccupancyIndexesSql("field_pole_occupancies"))).Should().Equal(
            """CREATE UNIQUE INDEX "AK_field_pole_occupancies_Id_OrganizationId" ON public.field_pole_occupancies USING btree ("Id", "OrganizationId")""",
            """CREATE UNIQUE INDEX "IX_field_pole_occupancies_OrganizationId_PoleFeatureId" ON public.field_pole_occupancies USING btree ("OrganizationId", "PoleFeatureId")""",
            """CREATE INDEX "IX_field_pole_occupancies_PoleFeatureId" ON public.field_pole_occupancies USING btree ("PoleFeatureId")""",
            """CREATE UNIQUE INDEX "PK_field_pole_occupancies" ON public.field_pole_occupancies USING btree ("Id")""");
        (await QueryAsync(db, OccupancyIndexesSql("field_pole_occupancy_entries"))).Should().Equal(
            """CREATE INDEX "IX_field_pole_occupancy_entries_OccupantOrganizationId" ON public.field_pole_occupancy_entries USING btree ("OccupantOrganizationId")""",
            """CREATE INDEX "IX_field_pole_occupancy_entries_OrganizationId" ON public.field_pole_occupancy_entries USING btree ("OrganizationId")""",
            """CREATE INDEX "IX_field_pole_occupancy_entries_PoleOccupancyId_OrganizationId" ON public.field_pole_occupancy_entries USING btree ("PoleOccupancyId", "OrganizationId")""",
            """CREATE UNIQUE INDEX "IX_field_pole_occupancy_entries_PoleOccupancyId_SlotIndex" ON public.field_pole_occupancy_entries USING btree ("PoleOccupancyId", "SlotIndex") WHERE ("SlotIndex" IS NOT NULL)""",
            """CREATE UNIQUE INDEX "PK_field_pole_occupancy_entries" ON public.field_pole_occupancy_entries USING btree ("Id")""");
        (await QueryAsync(db, OccupancyConstraintsSql("field_pole_occupancy_entries"))).Should().Equal(
            """CK_field_pole_occupancy_entries_SlotIndex CHECK ((("SlotIndex" IS NULL) OR (("SlotIndex" >= 1) AND ("SlotIndex" <= 6))))""",
            """FK_field_pole_occupancy_entries_field_pole_occupancies FOREIGN KEY ("PoleOccupancyId", "OrganizationId") REFERENCES field_pole_occupancies("Id", "OrganizationId") ON DELETE CASCADE""");
        (await QueryAsync(db, OccupancyRowLevelSecuritySql)).Should().Equal(
            ["field_pole_occupancies:false:false:0", "field_pole_occupancy_entries:false:false:0"],
            "row-level security arrives with the next migration");
    }

    [Fact]
    public async Task PoleOccupanciesMigrations_ApplyOverTheExistingSchema_ThenRollBackStepByStepAndReapply()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var tables = MigrationId(db, OccupancyTablesMigration);
        var rowLevelSecurity = MigrationId(db, OccupancyRowLevelSecurityMigration);
        var previous = chain[chain.IndexOf(tables) - 1];
        chain.IndexOf(rowLevelSecurity).Should().Be(chain.IndexOf(tables) + 1);

        // The schema every deployed database has today.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, OccupancyTablesExistSql)).Should().Equal("false:false");

        // Up: tables, then policies.
        await migrator.MigrateAsync(tables);
        await db.Database.ExecuteSqlRawAsync(InsertOccupancyPoleSql);
        await db.Database.ExecuteSqlRawAsync(InsertOccupancyEntrySql(1));
        await migrator.MigrateAsync(rowLevelSecurity);
        (await QueryAsync(db, OccupancyRowLevelSecuritySql)).Should().Equal(
            ["field_pole_occupancies:true:false:1", "field_pole_occupancy_entries:true:false:1"], "enabled, not forced, exactly one policy each");
        const string OwnerOnly = """("OrganizationId" = (current_setting('app.current_organization_id'::text, true))::uuid)""";
        (await QueryAsync(db, OccupancyPoliciesSql)).Should().Equal(
            $"field_pole_occupancies|tenant_isolation_policy|PERMISSIVE|ALL|{OwnerOnly}|{OwnerOnly}",
            $"field_pole_occupancy_entries|tenant_isolation_policy|PERMISSIVE|ALL|{OwnerOnly}|{OwnerOnly}");

        // Down, one step: the policies go, the tables and their rows stay.
        await migrator.MigrateAsync(tables);
        (await QueryAsync(db, OccupancyRowLevelSecuritySql)).Should().Equal(
            "field_pole_occupancies:false:false:0", "field_pole_occupancy_entries:false:false:0");
        (await QueryAsync(db, OccupancyEntryCountSql)).Should().Equal("1");

        // Down, second step: both tables go, nothing else does.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, OccupancyTablesExistSql)).Should().Equal("false:false");
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain([tables, rowLevelSecurity]);
        (await QueryAsync(db, """SELECT (to_regclass('public."ServiceOrderTargets"') IS NOT NULL)::text AS "Value" """)).Should().Equal("true");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, OccupancyRowLevelSecuritySql)).Should().Equal(
            "field_pole_occupancies:true:false:1", "field_pole_occupancy_entries:true:false:1");
    }

    [Fact]
    public async Task PoleOccupancies_InTheMigratedSchema_HoldAnyNumberOfEntriesBeyondTheSixSlots_ButOneEntryPerSlotOfTheirOwnPole()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(InsertOccupancyPoleSql);
        for (var slotIndex = 1; slotIndex <= 6; slotIndex++)
            await db.Database.ExecuteSqlRawAsync(InsertOccupancyEntrySql(slotIndex));

        // Over capacity: entries beyond the sixth slot are stored, however many.
        for (var beyond = 0; beyond < 5; beyond++)
            await db.Database.ExecuteSqlRawAsync(InsertOccupancyEntrySql(null));
        var secondOnSlotThree = () => db.Database.ExecuteSqlRawAsync(InsertOccupancyEntrySql(3));
        var slotSeven = () => db.Database.ExecuteSqlRawAsync(InsertOccupancyEntrySql(7));
        var slotZero = () => db.Database.ExecuteSqlRawAsync(InsertOccupancyEntrySql(0));
        var otherOrganization = () => db.Database.ExecuteSqlRawAsync(InsertOccupancyEntrySql(null, "d9d9d9d9-0000-0000-0000-000000000009"));

        (await QueryAsync(db, OccupancyEntryCountSql)).Should().Equal("11");
        (await secondOnSlotThree.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        (await slotSeven.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        (await slotZero.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        (await otherOrganization.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(
            PostgresErrorCodes.ForeignKeyViolation, "an entry must carry the organization of its pole");

        await db.Database.ExecuteSqlRawAsync("DELETE FROM field_pole_occupancies");
        (await QueryAsync(db, OccupancyEntryCountSql)).Should().Equal(["0"], "a pole's entries are deleted with it");
    }
}
