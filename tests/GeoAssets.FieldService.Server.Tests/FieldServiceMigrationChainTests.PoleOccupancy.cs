using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The XD01-314 pair <c>AddPoleOccupancies</c> / <c>AddPoleOccupanciesRowLevelSecurity</c> in the
/// <c>GeoAssets.FieldService.Server</c> history: the two pole occupancy tables as the migration
/// really creates them, up, down and up again, and their owner-only policies observed as a
/// non-owner role — including a grantee with a mirrored grant and an occupant, neither of which
/// may read them.
/// </summary>
public partial class FieldServiceMigrationChainTests
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

    // ── The policies, observed as a non-owner role ────────────────────────────

    private const string OccupancyScopedRole = "fieldservice_occupancy_scoped_role";

    private static readonly Guid OccupantOrganization = Guid.Parse("d3d3d3d3-0000-0000-0000-000000000003");

    [Fact]
    public async Task PoleOccupancyPolicies_AsANonOwnerRole_AreOwnerOnly_EvenForAMirroredGranteeAndForAnOccupant()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        var ownerPole = Guid.NewGuid();
        var strangerPole = Guid.NewGuid();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOccupancyPoleAsync(owner, ownerPole, "pole-owner", OwnerOrganization);
            await InsertOccupancyEntryAsync(owner, ownerPole, 1, OwnerOrganization, OccupantOrganization);
            await InsertOccupancyEntryAsync(owner, ownerPole, null, OwnerOrganization, GranteeOrganization);
            await InsertOccupancyEntryAsync(owner, ownerPole, null, OwnerOrganization, occupant: null);
            await InsertOccupancyPoleAsync(owner, strangerPole, "pole-stranger", StrangerOrganization);
            await InsertOccupancyEntryAsync(owner, strangerPole, 1, StrangerOrganization, OccupantOrganization);
            // An active, unexpired, untyped, unscoped grant from the owner to the grantee: the
            // widest one there is, and the one that opens the owner's orders and targets.
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsOccupancyScopedRoleAsync(db);

        (await VisibleOccupancyAsync(scoped, OwnerOrganization)).Should().Equal(
            "pole:pole-owner", "entry:pole-owner:1", "entry:pole-owner:-", "entry:pole-owner:-");
        (await VisibleOccupancyAsync(scoped, StrangerOrganization)).Should().Equal("pole:pole-stranger", "entry:pole-stranger:1");
        (await VisibleOccupancyAsync(scoped, GranteeOrganization)).Should().BeEmpty("no mirrored grant widens the owner-only occupancy policies");
        (await VisibleOccupancyAsync(scoped, OccupantOrganization)).Should().BeEmpty("an occupant does not see even the entries that name it");
    }

    [Fact]
    public async Task PoleOccupancyPolicies_AsANonOwnerRole_RejectWritesUnderAnotherOrganization_AndLetTheOwnerWrite()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        var ownerPole = Guid.NewGuid();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOccupancyPoleAsync(owner, ownerPole, "pole-owner", OwnerOrganization);
            await InsertOccupancyEntryAsync(owner, ownerPole, 1, OwnerOrganization, OccupantOrganization);
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsOccupancyScopedRoleAsync(db);

        foreach (var outsider in new[] { GranteeOrganization, OccupantOrganization, StrangerOrganization })
        {
            await SetOrganizationAsync(scoped, outsider);
            var forgedPole = async () => await InsertOccupancyPoleAsync(scoped, Guid.NewGuid(), "pole-forged", OwnerOrganization);
            var forgedEntry = async () => await InsertOccupancyEntryAsync(scoped, ownerPole, 6, OwnerOrganization, outsider);
            var ownOrganizationOnTheOwnersPole = async () => await InsertOccupancyEntryAsync(scoped, ownerPole, null, outsider, outsider);

            (await forgedPole.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
            (await forgedEntry.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
            (await ownOrganizationOnTheOwnersPole.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(
                PostgresErrorCodes.ForeignKeyViolation, "an entry must carry the organization of its pole");
            await using var tamper = scoped.CreateCommand();
            tamper.CommandText =
                """
                UPDATE field_pole_occupancies SET "Version" = 99;
                UPDATE field_pole_occupancy_entries SET "Status" = 'ClandestineUnauthorized';
                DELETE FROM field_pole_occupancy_entries;
                DELETE FROM field_pole_occupancies;
                """;
            (await tamper.ExecuteNonQueryAsync()).Should().Be(0, "none of the four statements may reach a row of another organization");
        }

        // The owner: six slots, then two entries beyond them, on a pole of its own.
        await SetOrganizationAsync(scoped, OwnerOrganization);
        var newPole = Guid.NewGuid();
        await InsertOccupancyPoleAsync(scoped, newPole, "pole-owner-2", OwnerOrganization);
        for (var slotIndex = 1; slotIndex <= 6; slotIndex++)
            await InsertOccupancyEntryAsync(scoped, newPole, slotIndex, OwnerOrganization, OccupantOrganization);
        await InsertOccupancyEntryAsync(scoped, newPole, null, OwnerOrganization, OccupantOrganization);
        await InsertOccupancyEntryAsync(scoped, newPole, null, OwnerOrganization, occupant: null);

        (await VisibleOccupancyAsync(scoped, OwnerOrganization)).Should().HaveCount(11, "two poles, the untouched seeded entry and the eight new ones");
    }

    /// <summary>
    /// A non-owner role holding exactly what the migration's comment says an application role
    /// needs for occupancy: privileges on the two tables, and on nothing else.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenAsOccupancyScopedRoleAsync(ServiceOrderDbContext db)
    {
        await using (var owner = await OpenAsync(db))
        {
            await using var grant = owner.CreateCommand();
            grant.CommandText = $"""
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OccupancyScopedRole}') THEN
                      CREATE ROLE {OccupancyScopedRole} LOGIN PASSWORD '{OccupancyScopedRole}';
                   END IF;
                END $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON field_pole_occupancies TO {OccupancyScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON field_pole_occupancy_entries TO {OccupancyScopedRole};
                """;
            await grant.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = OccupancyScopedRole,
            Password = OccupancyScopedRole,
            Pooling = false,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task InsertOccupancyPoleAsync(NpgsqlConnection connection, Guid id, string poleFeatureId, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO field_pole_occupancies ("Id", "PoleFeatureId", "OrganizationId", "Version", "UpdatedAt")
            VALUES (@id, @pole, @org, 1, now())
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("pole", poleFeatureId));
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task InsertOccupancyEntryAsync(NpgsqlConnection connection, Guid poleId, int? slotIndex, Guid organizationId, Guid? occupant)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO field_pole_occupancy_entries ("Id", "PoleOccupancyId", "SlotIndex", "Status", "OccupantOrganizationId", "CableDiameterMm", "CableLoadDaN", "OrganizationId")
            VALUES (gen_random_uuid(), @pole, @slot, 'AuthorizedContracted', @occupant, 12.5, 0, @org)
            """;
        insert.Parameters.Add(new NpgsqlParameter("pole", poleId));
        insert.Parameters.Add(new NpgsqlParameter("slot", NpgsqlTypes.NpgsqlDbType.Integer) { Value = slotIndex.HasValue ? slotIndex.Value : DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("occupant", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = occupant.HasValue ? occupant.Value : DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>Everything of both tables the session sees as <paramref name="organizationId"/>: poles first, then entries by slot.</summary>
    private static async Task<List<string>> VisibleOccupancyAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SetOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT 'pole:' || "PoleFeatureId" AS line, 0 AS kind, 0 AS slot FROM field_pole_occupancies
            UNION ALL
            SELECT 'entry:' || coalesce((SELECT p."PoleFeatureId" FROM field_pole_occupancies p WHERE p."Id" = e."PoleOccupancyId"), '?') || ':' || coalesce(e."SlotIndex"::text, '-'),
                   1, coalesce(e."SlotIndex", 99)
            FROM field_pole_occupancy_entries e
            ORDER BY kind, slot, line
            """;
        var lines = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return lines;
    }
}
