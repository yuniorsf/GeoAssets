using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-381 against the <c>GeoAssets.Server</c> history: the kernel's <c>DropFieldServiceTablesFromKernel</c> — which drops the five
/// FieldService tables (contractor US ledger, US catalog, postings, paving measurements, manhole audits) the shared-database chain
/// created but only <c>GeoAssets.FieldService.Server</c> ever used, and refuses while any of them holds a row — and the FieldService
/// chain's baseline that adopts them in <c>field_service</c>. The adoption assertions are <see cref="FieldServiceMigrationContract"/>'s,
/// shared with the other history.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string DropMigrationName = "DropFieldServiceTablesFromKernel";
    private const string MigratorRole = "kernel_drop_migrator_role";

    private static readonly SemaphoreSlim MigratorRoleLock = new(1, 1);

    public static TheoryData<string> FieldServiceTableNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var table in FieldServiceMigrationContract.Tables)
                data.Add(table);
            return data;
        }
    }

    // ── The adoption (shared with the FieldService history) ───────────────────

    [Fact]
    public Task FieldServiceBaseline_MovesTheTablesWithRowsContentIndexesConstraintsAndPolicies() =>
        FieldServiceMigrationContract.AssertTheMoveKeepsRowsContentIndexesConstraintsAndPoliciesAsync(NewContextOnEmptyDatabaseAsync, kernelHeadBeforeTheMove: false);

    [Fact]
    public Task FieldServiceBaseline_AppliedAgain_IsANoOp() =>
        FieldServiceMigrationContract.AssertReapplyingTheBaselineIsANoOpAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_WithAStrayTableInBothSchemas_RaisesAndChangesNothing() =>
        FieldServiceMigrationContract.AssertAStrayTableInBothSchemasRaisesAndChangesNothingAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceChain_WithoutTheKernelChain_Raises() =>
        FieldServiceMigrationContract.AssertTheChainWithoutTheKernelChainRaisesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_OnAFreshDatabase_GivesEmptyTablesWithRowLevelSecurityAndPolicies() =>
        FieldServiceMigrationContract.AssertAFreshDatabaseGetsEmptyTablesWithRowLevelSecurityAndPoliciesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_CreatePath_BuildsTheSameShapeAsTheMovedPath() =>
        FieldServiceMigrationContract.AssertTheCreatePathBuildsTheSameShapeAsTheMovedPathAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_Down_MovesTheTablesBackWithTheirData_AndUpRestoresThem() =>
        FieldServiceMigrationContract.AssertDownMovesTheTablesBackWithTheirDataAndUpRestoresThemAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceBaseline_Down_RefusesWhilePublicHoldsASameNamedTable() =>
        FieldServiceMigrationContract.AssertDownRefusesWhilePublicHoldsASameNamedTableAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceTables_AfterTheMove_KeepTheirRowLevelSecurity_AsANonOwnerRole() =>
        FieldServiceMigrationContract.AssertRowLevelSecurityHoldsOnTheMovedTablesAsync(NewContextOnEmptyDatabaseAsync);

    [Fact]
    public Task FieldServiceModels_OnMigratedDatabases_HaveNoPendingChanges_AndTheKernelModelHasNoFieldServiceEntities() =>
        FieldServiceMigrationContract.AssertTheKernelModelHasNoFieldServiceEntitiesAndBothModelsMatchTheirSnapshotsAsync(NewContextOnEmptyDatabaseAsync);

    // ── DropFieldServiceTablesFromKernel ──────────────────────────────────────

    [Fact]
    public async Task DropFieldServiceTablesFromKernel_OverEmptyTables_DropsAllFiveWithTheirPolicies_DownRecreatesThemIdentically_UpDropsAgain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var (drop, previous) = DropAndPreviousMigrations(db);
        await migrator.MigrateAsync(previous);
        foreach (var table in FieldServiceMigrationContract.Tables)
            (await FieldServiceMigrationContract.ExistsAsync(db, "public", table)).Should().BeTrue($"{table} exists before the drop");
        var fingerprint = await FieldServiceMigrationContract.SnapshotAsync(db, "public");
        var relations = await FieldServiceMigrationContract.RelationsAsync(db);
        fingerprint.Values.Should().OnlyContain(f => f.StartsWith("rows 0\n", StringComparison.Ordinal), "nothing was ever written to the kernel's copies");
        (await QueryAsync(db, PoliciesOfTheFiveSql)).Should().HaveCount(4, "the Server chain gave a policy to four of them — never to the postings");

        // Up: the tables, their indexes and constraints and their policies are gone.
        await migrator.MigrateAsync(drop);

        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(drop);
        foreach (var table in FieldServiceMigrationContract.Tables)
            (await FieldServiceMigrationContract.ExistsAsync(db, "public", table)).Should().BeFalse($"{table} was dropped");
        (await QueryAsync(db, PoliciesOfTheFiveSql)).Should().BeEmpty("the policies went with their tables");
        (await FieldServiceMigrationContract.RelationsAsync(db)).Should().NotContain(
            relation => FieldServiceMigrationContract.Tables.Any(table => relation.Contains(table, StringComparison.Ordinal)),
            "no table, index or constraint index of the five is left behind");
        (await QueryAsync(db, ForeignKeysToTheOrdersSql)).Should().BeEmpty("the foreign keys to \"ServiceOrders\" went with the tables");

        // Down: the five empty tables are back, identical in columns, indexes, constraints, RLS flags and policies.
        await migrator.MigrateAsync(previous);

        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(drop);
        (await FieldServiceMigrationContract.SnapshotAsync(db, "public")).Should().BeEquivalentTo(fingerprint);
        (await FieldServiceMigrationContract.RelationsAsync(db)).Should().Equal(relations);
        (await QueryAsync(db, PoliciesOfTheFiveSql)).Should().HaveCount(4);

        // Up again: gone again.
        await db.Database.MigrateAsync();

        foreach (var table in FieldServiceMigrationContract.Tables)
            (await FieldServiceMigrationContract.ExistsAsync(db, "public", table)).Should().BeFalse();
        (await QueryAsync(db, PoliciesOfTheFiveSql)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FieldServiceTableNames))]
    public async Task DropFieldServiceTablesFromKernel_WhenATableHoldsRows_RaisesNamingTheTableAndItsRowCount_AndDropsNothing(string table)
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var (drop, previous) = DropAndPreviousMigrations(db);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await InsertRowsAsync(db, table, 2);

        var up = () => db.GetService<IMigrator>().MigrateAsync(drop);

        (await up.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(
            $"Cannot drop {table} from the kernel database: it holds 2 row(s). Move them to the FieldService database first.");
        await AssertNothingWasDroppedAsync(db, drop, (table, 2));
    }

    [Fact]
    public async Task DropFieldServiceTablesFromKernel_WithRowsInSeveralTables_NamesTheFirstTableItChecks_AndDropsNothing()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var (drop, previous) = DropAndPreviousMigrations(db);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await InsertRowsAsync(db, FieldServiceMigrationContract.Catalog, 1);
        await InsertRowsAsync(db, FieldServiceMigrationContract.Paving, 3);

        var up = () => db.GetService<IMigrator>().MigrateAsync(drop);

        (await up.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain(
            $"Cannot drop {FieldServiceMigrationContract.Paving} from the kernel database: it holds 3 row(s).",
            "the paving measurements come before the catalog in the order the migration checks");
        await AssertNothingWasDroppedAsync(db, drop, (FieldServiceMigrationContract.Paving, 3), (FieldServiceMigrationContract.Catalog, 1));
    }

    [Fact]
    public async Task DropFieldServiceTablesFromKernel_WhenARowIsHiddenFromTheMigratingRoleByForcedRowLevelSecurity_FailsLoudly_NotCountingItAsZero()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var (drop, previous) = DropAndPreviousMigrations(db);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await InsertRowsAsync(db, FieldServiceMigrationContract.Ledger, 1);
        await GrantTheMigratorTheKernelDatabaseAsync(db, forcedTable: FieldServiceMigrationContract.Ledger);

        // The control: as the migrating role, the forced policy hides the row from a plain count — "no row" would let the drop through.
        await using (var control = await OpenMigratorConnectionAsync(db))
        {
            await using var count = control.CreateCommand();
            count.CommandText = """SELECT count(*) FROM "ContractorUsLedgerEntries" """;
            (await count.ExecuteScalarAsync()).Should().Be(0L, "the row belongs to an organization the migrating session does not name");
        }

        await using var asTheMigrator = new ServiceOrderDbContext(
            SecurityTestContainerFixture.BuildServiceOrderOptions(MigratorConnectionString(db)));
        var up = () => asTheMigrator.GetService<IMigrator>().MigrateAsync(drop);

        var failure = (await up.Should().ThrowAsync<PostgresException>()).Which;
        failure.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege,
            "with row_security off, a query the policy would filter is refused instead of silently returning fewer rows");
        failure.MessageText.Should().Contain("row-level security").And.Contain("ContractorUsLedgerEntries", "the refusal is the policy's, not a missing grant");
        await AssertNothingWasDroppedAsync(db, drop, (FieldServiceMigrationContract.Ledger, 1));
    }

    [Fact]
    public async Task DropFieldServiceTablesFromKernel_AfterTheTablesWereMovedToFieldService_DropsNothingThereAndKeepsTheRows()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await FieldServiceMigrationContract.MigrateKernelToTheLastMigrationHoldingTheTablesAsync(db);
        await FieldServiceMigrationContract.SeedAsync(db);
        await using var fieldService = FieldServiceMigrationContract.NewFieldService(db);
        await fieldService.Database.MigrateAsync();
        var moved = await FieldServiceMigrationContract.SnapshotAsync(db, FieldServiceMigrationContract.FieldServiceSchema);

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await FieldServiceMigrationContract.SnapshotAsync(db, FieldServiceMigrationContract.FieldServiceSchema)).Should().BeEquivalentTo(moved,
            "the guarded drop looks in public only: what an operator moved first is untouched");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string PoliciesOfTheFiveSql =
        """
        SELECT (tablename || ':' || policyname)::text AS "Value" FROM pg_policies
        WHERE schemaname = 'public'
          AND tablename IN ('ContractorUsLedgerEntries', 'UsCatalogItems', 'field_contractor_us_postings', 'field_icerp_paving_measurements', 'field_manhole_audits')
        ORDER BY tablename COLLATE "C"
        """;

    private const string ForeignKeysToTheOrdersSql =
        """
        SELECT conname::text AS "Value" FROM pg_constraint
        WHERE conname LIKE 'FK_field_contractor_us_postings_%' OR conname LIKE 'FK_field_icerp_paving_measurements_%'
        """;

    private static (string Drop, string Previous) DropAndPreviousMigrations(ServiceOrderDbContext db)
    {
        var chain = db.Database.GetMigrations().ToList();
        var drop = chain.Single(id => id.EndsWith("_" + DropMigrationName, StringComparison.Ordinal));
        var previous = chain[chain.IndexOf(drop) - 1];
        previous.Should().Be(FieldServiceMigrationContract.KernelMigrationBeforeXD01381(db));
        return (drop, previous);
    }

    /// <summary>Everything the guarded drop must not have touched when it raised: the five tables, every row, the history.</summary>
    private static async Task AssertNothingWasDroppedAsync(ServiceOrderDbContext db, string drop, params (string Table, int Rows)[] expectedRows)
    {
        foreach (var table in FieldServiceMigrationContract.Tables)
        {
            (await FieldServiceMigrationContract.ExistsAsync(db, "public", table)).Should().BeTrue($"{table} survived the refused drop");
            var expected = expectedRows.Where(e => string.Equals(e.Table, table, StringComparison.Ordinal)).Select(e => e.Rows).SingleOrDefault();
            (await QueryAsync(db, FieldServiceMigrationContract.CountSql("public", table))).Should().Equal(
                [expected.ToString(System.Globalization.CultureInfo.InvariantCulture)], $"{table} kept its rows");
        }

        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(drop, "the history did not advance");
        (await QueryAsync(db, PoliciesOfTheFiveSql)).Should().HaveCount(4, "the policies are still there");
    }

    private static string QuotedTable(string table) =>
        string.Equals(table, FieldServiceMigrationContract.Ledger, StringComparison.Ordinal)
        || string.Equals(table, FieldServiceMigrationContract.Catalog, StringComparison.Ordinal)
            ? $"public.\"{table}\""
            : $"public.{table}";

    /// <summary>Puts <paramref name="rows"/> rows of organization X, written as the table owner, in one kernel copy of the five tables.</summary>
    private static async Task InsertRowsAsync(ServiceOrderDbContext db, string table, int rows)
    {
        var org = FieldServiceMigrationContract.OrgX;
        await FieldServiceMigrationContract.ExecuteAsync(db,
            """
            INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
            VALUES ('drop-ord', 'Order', '', 'inspection', 'Draft', 0, 'tester', @org, now(), '{}', '[]')
            ON CONFLICT ("Id") DO NOTHING
            """,
            ("org", org));

        for (var i = 0; i < rows; i++)
        {
            var sql = table switch
            {
                FieldServiceMigrationContract.Ledger =>
                    """INSERT INTO @T ("Id", "OrganizationId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt") VALUES (@id, @org, 'US-1', '', 1, 1, '[]', now())""",
                FieldServiceMigrationContract.Catalog =>
                    """INSERT INTO @T ("Id", "OrganizationId", "ItemCode", "Description", "Unit", "BaseUsFactor", "RegionalMultiplier", "MonetaryConversionRate", "EffectiveFrom") VALUES (@id, @org, 'ITEM-1', '', 'un', 1, 1, 1, now())""",
                FieldServiceMigrationContract.Postings =>
                    """INSERT INTO @T ("Id", "ServiceOrderId", "ContractorOrganizationId", "ItemCode", "Quantity", "UnitUs", "TotalUs", "MonetaryRate", "TotalBrl", "PostedAtUtc") VALUES (@id, 'drop-ord', @org, 'ITEM-1', 1, 1, 1, 1, 1, now())""",
                FieldServiceMigrationContract.Paving =>
                    """INSERT INTO @T ("Id", "ParentServiceOrderId", "ChildServiceOrderId", "OrganizationId", "LengthMeters", "WidthMeters", "DepthMeters", "RecordedAreaSquareMeters", "ActualAreaSquareMeters", "VolumeCubicMeters", "RecordedAtUtc") VALUES (@id, 'drop-ord', 'drop-ord', @org, 1, 1, 1, 1, 1, 1, now())""",
                FieldServiceMigrationContract.Manholes =>
                    """INSERT INTO @T ("Id", "ConduitFeatureId", "OrganizationId", "BoreOccupancyPercentage", "SubDuctsJson", "SpliceEnclosureCondition", "Nr33ChecklistAttributesJson", "AuditedBy", "AuditedAtUtc") VALUES (@id, 'conduit-1', @org, 10, '[]', 'ok', '{}', 'tester', now())""",
                _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not one of the five FieldService tables."),
            };
            await FieldServiceMigrationContract.ExecuteAsync(
                db, sql.Replace("@T", QuotedTable(table), StringComparison.Ordinal), ("id", Guid.NewGuid()), ("org", org));
        }
    }

    /// <summary>
    /// Makes a non-superuser role — which does not bypass row-level security — the owner of the five kernel copies, forces
    /// row-level security on <paramref name="forcedTable"/> (the one holding the hidden row; forced on an empty table it would already refuse the query), and gives it what a migration run needs besides (the history table, and CREATE on the schema).
    /// </summary>
    private static async Task GrantTheMigratorTheKernelDatabaseAsync(ServiceOrderDbContext db, string forcedTable)
    {
        await MigratorRoleLock.WaitAsync();
        try
        {
            await using var owner = new NpgsqlConnection(db.Database.GetConnectionString());
            await owner.OpenAsync();
            await using var setup = owner.CreateCommand();
            setup.CommandText = $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{MigratorRole}') THEN
                        CREATE ROLE {MigratorRole} LOGIN PASSWORD '{MigratorRole}' NOSUPERUSER NOBYPASSRLS;
                    END IF;
                END $$;
                """;
            await setup.ExecuteNonQueryAsync();
        }
        finally
        {
            MigratorRoleLock.Release();
        }

        foreach (var table in FieldServiceMigrationContract.Tables)
            await FieldServiceMigrationContract.ExecuteAsync(db, $"ALTER TABLE {QuotedTable(table)} OWNER TO {MigratorRole}");
        await FieldServiceMigrationContract.ExecuteAsync(db, $"ALTER TABLE {QuotedTable(forcedTable)} FORCE ROW LEVEL SECURITY");
        await FieldServiceMigrationContract.ExecuteAsync(db, $"""GRANT SELECT, INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" TO {MigratorRole}""");
        // EF's history repository issues CREATE TABLE IF NOT EXISTS before reading it, which asks for CREATE on the schema even when the table is there.
        await FieldServiceMigrationContract.ExecuteAsync(db, $"GRANT CREATE ON SCHEMA public TO {MigratorRole}");
    }

    private static string MigratorConnectionString(ServiceOrderDbContext db) =>
        new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = MigratorRole,
            Password = MigratorRole,
            Pooling = false,
        }.ConnectionString;

    private static async Task<NpgsqlConnection> OpenMigratorConnectionAsync(ServiceOrderDbContext db)
    {
        var connection = new NpgsqlConnection(MigratorConnectionString(db));
        await connection.OpenAsync();
        return connection;
    }
}
