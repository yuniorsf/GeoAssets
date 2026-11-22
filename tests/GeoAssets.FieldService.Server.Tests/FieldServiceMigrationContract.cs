using FluentAssertions;
using GeoAssets.FieldService.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GeoAssets.Workflow.Persistence.Contracts;

/// <summary>
/// What moving the FieldService tables out of the kernel's <see cref="ServiceOrderDbContext"/> (XD01-381) must do to a
/// PostgreSQL database, whichever kernel migration history it carries. The FieldService chain's baseline
/// (<c>AdoptFieldServiceTables</c>) moves <c>"ContractorUsLedgerEntries"</c>, <c>"UsCatalogItems"</c>,
/// <c>field_contractor_us_postings</c>, <c>field_icerp_paving_measurements</c> and <c>field_manhole_audits</c> from
/// <c>public</c> into <c>field_service</c> with their rows, indexes, constraints (the foreign keys to
/// <c>public."ServiceOrders"</c> included), row-level-security flag and policies — and refuses or creates in the cases it was
/// written to. One body, compiled into both suites that own a history for <see cref="ServiceOrderDbContext"/> —
/// GeoAssets.FieldService.Server.Tests (the isolated FieldService database) and GeoAssets.Security.Tests (which links this
/// file; the shared GeoAssets.Server one) — so both are held to the same assertions.
///
/// <para>The two histories differ in what their kernel migration of XD01-381 does: the FieldService one only changes the
/// model (<c>RemoveFieldServiceTablesFromKernelModel</c>, no SQL), the Server one <b>drops</b> the tables, after refusing
/// when any holds a row (<c>DropFieldServiceTablesFromKernel</c>). So every scenario that needs the tables in <c>public</c>
/// stops the kernel chain one migration earlier (<see cref="MigrateKernelToTheLastMigrationHoldingTheTablesAsync"/>) and moves
/// the tables first — the order an operator follows. Where a scenario runs the kernel head <i>before</i> the move it says so,
/// and only the FieldService history may.</para>
///
/// Each member takes a factory of kernel contexts on a freshly named, not yet migrated database.
/// </summary>
internal static class FieldServiceMigrationContract
{
    public const string BaselineName = "AdoptFieldServiceTables";
    public const string FieldServiceModelMigrationName = "RemoveFieldServiceTablesFromKernelModel";
    public const string ServerDropMigrationName = "DropFieldServiceTablesFromKernel";
    public const string OccupancyMigrationName = "RemoveOccupancyFromKernelModel";

    public const string Ledger = "ContractorUsLedgerEntries";
    public const string Catalog = "UsCatalogItems";
    public const string Postings = "field_contractor_us_postings";
    public const string Paving = "field_icerp_paving_measurements";
    public const string Manholes = "field_manhole_audits";
    public const string FieldServiceSchema = "field_service";
    public const string PolicyName = "tenant_isolation_policy";

    /// <summary>The tables in the order the baseline's loops handle them: the last one is the one a stray copy must trip over.</summary>
    public static readonly string[] Tables = [Ledger, Catalog, Postings, Paving, Manholes];

    private const string ScopedRole = "field_service_contract_scoped_role";
    private static readonly SemaphoreSlim RoleLock = new(1, 1);

    public static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000381");
    public static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000381");

    // ── The moved path ────────────────────────────────────────────────────────

    /// <summary>
    /// Rows (count and checksum), content, indexes, constraints, the row-level-security flags and the policies of all five
    /// tables are identical after the move; the postings' and paving measurements' foreign keys to the kernel's orders are
    /// still there and still refuse the deletion of a referenced order.
    /// </summary>
    /// <param name="kernelHeadBeforeTheMove">
    /// Applies the kernel head first, then the baseline — only legal where the kernel head leaves the tables in <c>public</c>
    /// (the FieldService history). Otherwise the baseline runs first and the kernel head after it, which is what an operator
    /// of the Server history does: move, then let the guarded drop find nothing in <c>public</c>.
    /// </param>
    public static async Task AssertTheMoveKeepsRowsContentIndexesConstraintsAndPoliciesAsync(
        Func<Task<ServiceOrderDbContext>> newEmptyDatabase, bool kernelHeadBeforeTheMove)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToTheLastMigrationHoldingTheTablesAsync(db);
        await SeedAsync(db);
        var before = await SnapshotAsync(db, "public");
        var postingsHadPolicy = before[Postings].Contains($"policy {PolicyName}|", StringComparison.Ordinal);
        before[Ledger].Should().StartWith("rows 3\n");
        before[Catalog].Should().StartWith("rows 3\n");
        before[Postings].Should().StartWith("rows 3\n");
        before[Paving].Should().StartWith("rows 2\n");
        before[Manholes].Should().StartWith("rows 3\n");

        await using var fieldService = NewFieldService(db);
        if (kernelHeadBeforeTheMove)
        {
            await db.Database.MigrateAsync();
            (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(before, "the kernel's head migration touches nothing in this history");
            await fieldService.Database.MigrateAsync();
        }
        else
        {
            await fieldService.Database.MigrateAsync();
            await db.Database.MigrateAsync();
        }

        foreach (var table in Tables)
            (await ExistsAsync(db, "public", table)).Should().BeFalse($"{table} left public");
        var after = await SnapshotAsync(db, FieldServiceSchema);
        foreach (var table in Tables)
        {
            var expected = IsUnsecuredPostings(table, postingsHadPolicy) ? WithoutSecurity(before[table]) : before[table];
            var actual = IsUnsecuredPostings(table, postingsHadPolicy) ? WithoutSecurity(after[table]) : after[table];
            actual.Should().Be(expected, $"{table} moved with every row, index, constraint, flag and policy");
        }

        foreach (var table in Tables)
        {
            (await QueryAsync(db, PolicyCountSql(FieldServiceSchema, table))).Should().Equal("1");
            (await QueryAsync(db, RowSecuritySql(FieldServiceSchema, table))).Should().Equal(["true:false"], "enabled, not forced");
            CountLines(after[table], "index ").Should().Be(CountLines(before[table], "index "), $"no index of {table} was duplicated or lost");
            CountLines(after[table], "constraint ").Should().Be(CountLines(before[table], "constraint "), $"no constraint of {table} was duplicated or lost");
        }

        after[Postings].Should().Contain($"policy {PolicyName}|PERMISSIVE|ALL|(\"ContractorOrganizationId\" = ");
        (await QueryAsync(db, ForeignKeysSql(Postings))).Should().Equal(
            """FK_field_contractor_us_postings_ServiceOrders_ServiceOrderId FOREIGN KEY ("ServiceOrderId") REFERENCES "ServiceOrders"("Id") ON DELETE RESTRICT""");
        (await QueryAsync(db, ForeignKeysSql(Paving))).Should().Equal(
            """FK_field_icerp_paving_measurements_ServiceOrders_Child FOREIGN KEY ("ChildServiceOrderId") REFERENCES "ServiceOrders"("Id") ON DELETE RESTRICT""",
            """FK_field_icerp_paving_measurements_ServiceOrders_Parent FOREIGN KEY ("ParentServiceOrderId") REFERENCES "ServiceOrders"("Id") ON DELETE RESTRICT""");
        (await QueryAsync(db, """SELECT "MigrationId"::text AS "Value" FROM field_service."__EFMigrationsHistory_field_service" """))
            .Should().ContainSingle().Which.Should().EndWith("_" + BaselineName);
        (await fieldService.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        fieldService.Database.HasPendingModelChanges().Should().BeFalse("the shipped snapshot is the model");

        // The foreign keys keep guarding the kernel's orders from across the schemas: a referenced order cannot be deleted ...
        var deleteReferencedOrder = async () => await ExecuteAsync(db, """DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-1'""");
        (await deleteReferencedOrder.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        // ... and a posting cannot reference an order that is not there.
        var danglingPosting = async () => await ExecuteAsync(db, InsertPostingSql, ("id", Guid.NewGuid()), ("order", "no-such-order"), ("org", OrgX));
        (await danglingPosting.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        (await QueryAsync(db, CountSql(FieldServiceSchema, Postings))).Should().Equal("3");
    }

    // ── Baseline: idempotency and guards ──────────────────────────────────────

    /// <summary>Running the baseline again (its history row deleted) is a no-op: same counts, checksums, content, policies.</summary>
    public static async Task AssertReapplyingTheBaselineIsANoOpAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToTheLastMigrationHoldingTheTablesAsync(db);
        await SeedAsync(db);
        await using var fieldService = NewFieldService(db);
        await fieldService.Database.MigrateAsync();
        var applied = await SnapshotAsync(db, FieldServiceSchema);

        await ExecuteAsync(db, """DELETE FROM field_service."__EFMigrationsHistory_field_service" """);
        (await fieldService.Database.GetPendingMigrationsAsync()).Should().ContainSingle();
        await fieldService.Database.MigrateAsync();

        (await SnapshotAsync(db, FieldServiceSchema)).Should().BeEquivalentTo(applied);
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM field_service."__EFMigrationsHistory_field_service" """)).Should().Equal("1");
        foreach (var table in Tables)
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
    }

    /// <summary>A same-named table already in <c>field_service</c> while the real one is in <c>public</c>: the baseline raises and nothing changes.</summary>
    public static async Task AssertAStrayTableInBothSchemasRaisesAndChangesNothingAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToTheLastMigrationHoldingTheTablesAsync(db);
        await SeedAsync(db);
        var before = await SnapshotAsync(db, "public");
        // The last table of the baseline's loop: the first four are moved before it is reached, so the whole step must roll back.
        await ExecuteAsync(db, "CREATE SCHEMA IF NOT EXISTS field_service");
        await ExecuteAsync(db, $"CREATE TABLE field_service.{Manholes} (stray integer)");
        await ExecuteAsync(db, $"INSERT INTO field_service.{Manholes} VALUES (7)");
        await using var fieldService = NewFieldService(db);

        var migrate = () => fieldService.Database.MigrateAsync();

        (await migrate.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("exists in both public and field_service").And.Contain(Manholes);
        (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(before, "the real tables stayed where they were, rows included");
        foreach (var table in Tables.Take(4))
            (await ExistsAsync(db, FieldServiceSchema, table)).Should().BeFalse($"the move of {table} was rolled back with the step");
        (await QueryAsync(db, $"""SELECT stray::text AS "Value" FROM field_service.{Manholes} """)).Should().Equal(["7"], "the stray table is untouched");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM field_service."__EFMigrationsHistory_field_service" """)).Should().Equal("0");
    }

    /// <summary>The FieldService chain on a database the kernel chain never ran on: refuses, creates nothing.</summary>
    public static async Task AssertTheChainWithoutTheKernelChainRaisesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await using var fieldService = NewFieldService(db);

        var migrate = () => fieldService.Database.MigrateAsync();

        (await migrate.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should()
            .Contain("public.\"ServiceOrders\" is missing").And.Contain("kernel ServiceOrderDbContext migrations");
        foreach (var table in Tables)
        {
            (await ExistsAsync(db, FieldServiceSchema, table)).Should().BeFalse();
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
        }

        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM field_service."__EFMigrationsHistory_field_service" """)).Should().Equal("0");
    }

    /// <summary>A fresh database: the full kernel chain, then the baseline — empty tables in <c>field_service</c> with row-level security, policies and the foreign keys to the orders.</summary>
    public static async Task AssertAFreshDatabaseGetsEmptyTablesWithRowLevelSecurityAndPoliciesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await db.Database.MigrateAsync();
        await using var fieldService = NewFieldService(db);

        await fieldService.Database.MigrateAsync();

        var shape = await SnapshotAsync(db, FieldServiceSchema);
        foreach (var table in Tables)
        {
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
            (await QueryAsync(db, CountSql(FieldServiceSchema, table))).Should().Equal("0");
            (await QueryAsync(db, RowSecuritySql(FieldServiceSchema, table))).Should().Equal("true:false");
            (await QueryAsync(db, PolicyCountSql(FieldServiceSchema, table))).Should().Equal("1");
            shape[table].Should().Contain($"{PolicyName}|PERMISSIVE|ALL|");
        }

        shape[Postings].Should().Contain("(\"ContractorOrganizationId\" = (current_setting('app.current_organization_id'::text, true))::uuid)");
        foreach (var table in new[] { Ledger, Catalog, Paving, Manholes })
            shape[table].Should().Contain("(\"OrganizationId\" = (current_setting('app.current_organization_id'::text, true))::uuid)");

        (await QueryAsync(db, IndexNamesSql(FieldServiceSchema, Ledger))).Should().Equal(
            "IX_ContractorUsLedgerEntries_OrganizationId", "IX_ContractorUsLedgerEntries_ServiceOrderId", "PK_ContractorUsLedgerEntries");
        (await QueryAsync(db, IndexNamesSql(FieldServiceSchema, Catalog))).Should().Equal(
            "IX_UsCatalogItems_OrganizationId_ItemCode", "PK_UsCatalogItems");
        (await QueryAsync(db, IndexNamesSql(FieldServiceSchema, Postings))).Should().Equal(
            "IX_field_contractor_us_postings_ContractorOrganizationId_Poste~", "IX_field_contractor_us_postings_ServiceOrderId",
            "PK_field_contractor_us_postings");
        (await QueryAsync(db, IndexNamesSql(FieldServiceSchema, Paving))).Should().Equal(
            "IX_field_icerp_paving_measurements_ChildServiceOrderId", "IX_field_icerp_paving_measurements_OrganizationId",
            "IX_field_icerp_paving_measurements_ParentServiceOrderId", "PK_field_icerp_paving_measurements");
        (await QueryAsync(db, IndexNamesSql(FieldServiceSchema, Manholes))).Should().Equal(
            "IX_field_manhole_audits_ConduitFeatureId", "IX_field_manhole_audits_OrganizationId", "PK_field_manhole_audits");
        (await QueryAsync(db, ForeignKeysSql(Postings))).Should().ContainSingle().Which.Should().Contain("ON DELETE RESTRICT");
        (await QueryAsync(db, ForeignKeysSql(Paving))).Should().HaveCount(2).And.OnlyContain(fk => fk.Contains("ON DELETE RESTRICT", StringComparison.Ordinal));
        (await QueryAsync(db, ForeignKeysSql(Ledger))).Should().BeEmpty("the ledger never had a foreign key");
    }

    /// <summary>Tables in neither schema (the kernel's copies dropped) with the kernel's orders present: created, in the same shape as the moved ones.</summary>
    public static async Task AssertTheCreatePathBuildsTheSameShapeAsTheMovedPathAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var moved = await newEmptyDatabase();
        await MigrateKernelToTheLastMigrationHoldingTheTablesAsync(moved);
        await using var movedFieldService = NewFieldService(moved);
        await movedFieldService.Database.MigrateAsync();
        var movedShape = await SnapshotAsync(moved, FieldServiceSchema);

        await using var created = await newEmptyDatabase();
        await created.Database.MigrateAsync();
        foreach (var table in Tables)
            await ExecuteAsync(created, $"""DROP TABLE IF EXISTS public."{table}" CASCADE;""");
        foreach (var table in Tables)
            (await ExistsAsync(created, "public", table)).Should().BeFalse();
        await using var createdFieldService = NewFieldService(created);

        await createdFieldService.Database.MigrateAsync();

        (await SnapshotAsync(created, FieldServiceSchema)).Should().BeEquivalentTo(movedShape, "creating gives what moving gives, policies and flags included");
        foreach (var table in Tables)
            (await ExistsAsync(created, "public", table)).Should().BeFalse("the create path builds in field_service only");
    }

    // ── Down ──────────────────────────────────────────────────────────────────

    /// <summary>Down moves the tables back to <c>public</c> with data, flags and policies; up again restores.</summary>
    public static async Task AssertDownMovesTheTablesBackWithTheirDataAndUpRestoresThemAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToTheLastMigrationHoldingTheTablesAsync(db);
        await SeedAsync(db);
        var original = await SnapshotAsync(db, "public");
        var postingsHadPolicy = original[Postings].Contains($"policy {PolicyName}|", StringComparison.Ordinal);
        await using var fieldService = NewFieldService(db);
        await fieldService.Database.MigrateAsync();
        var moved = await SnapshotAsync(db, FieldServiceSchema);

        await fieldService.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);

        (await fieldService.Database.GetAppliedMigrationsAsync()).Should().BeEmpty();
        foreach (var table in Tables)
            (await ExistsAsync(db, FieldServiceSchema, table)).Should().BeFalse($"{table} went back");
        var back = await SnapshotAsync(db, "public");
        foreach (var table in Tables)
        {
            // The baseline put a policy (and the flag) on the postings the Server history never gave one; they stay with the table.
            var expected = IsUnsecuredPostings(table, postingsHadPolicy) ? WithoutSecurity(original[table]) : original[table];
            var actual = IsUnsecuredPostings(table, postingsHadPolicy) ? WithoutSecurity(back[table]) : back[table];
            actual.Should().Be(expected, "rows, indexes, constraints, flags and policies came back with the tables");
        }

        await fieldService.Database.MigrateAsync();

        (await SnapshotAsync(db, FieldServiceSchema)).Should().BeEquivalentTo(moved);
        foreach (var table in Tables)
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
    }

    /// <summary>Down refuses (changing nothing) while <c>public</c> holds a same-named table; with it gone, it goes through.</summary>
    public static async Task AssertDownRefusesWhilePublicHoldsASameNamedTableAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToTheLastMigrationHoldingTheTablesAsync(db);
        await SeedAsync(db);
        await using var fieldService = NewFieldService(db);
        await fieldService.Database.MigrateAsync();
        var moved = await SnapshotAsync(db, FieldServiceSchema);
        // Down's check runs before any move, so one stray copy of any table refuses the whole step.
        await ExecuteAsync(db, $"CREATE TABLE public.{Manholes} (stray integer)");

        var down = () => fieldService.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("exists in both public and field_service").And.Contain(Manholes);
        (await SnapshotAsync(db, FieldServiceSchema)).Should().BeEquivalentTo(moved, "the whole step rolled back, data included");
        foreach (var table in Tables.Take(4))
            (await ExistsAsync(db, "public", table)).Should().BeFalse($"{table} did not move");
        (await fieldService.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();

        await ExecuteAsync(db, $"DROP TABLE public.{Manholes}");
        await fieldService.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        (await ExistsAsync(db, "public", Manholes)).Should().BeTrue();
    }

    // ── Row-level security after the move ─────────────────────────────────────

    /// <summary>
    /// As a non-owner role on the moved tables: the owning organization sees its rows, another sees none, and a write under
    /// another organization's name is rejected by the policy's WITH CHECK — the postings by <c>"ContractorOrganizationId"</c>.
    /// </summary>
    public static async Task AssertRowLevelSecurityHoldsOnTheMovedTablesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToTheLastMigrationHoldingTheTablesAsync(db);
        await SeedAsync(db);
        await using var fieldService = NewFieldService(db);
        await fieldService.Database.MigrateAsync();
        await using var scoped = await OpenScopedRoleAsync(db);

        foreach (var table in Tables)
            (await ReadIdsAsync(scoped, table)).Should().BeEmpty($"a session that named no organization sees nothing of {table}");

        await SetOrganizationAsync(scoped, OrgX);
        (await ReadIdsAsync(scoped, Ledger)).Should().HaveCount(2);
        (await ReadIdsAsync(scoped, Catalog)).Should().HaveCount(2);
        (await ReadIdsAsync(scoped, Postings)).Should().HaveCount(2, "the postings are scoped by their ContractorOrganizationId");
        (await ReadIdsAsync(scoped, Paving)).Should().HaveCount(1);
        (await ReadIdsAsync(scoped, Manholes)).Should().HaveCount(2);

        await SetOrganizationAsync(scoped, OrgY);
        foreach (var table in Tables)
            (await ReadIdsAsync(scoped, table)).Should().HaveCount(1, $"organization Y owns one row of {table}");

        await SetOrganizationAsync(scoped, Guid.NewGuid());
        foreach (var table in Tables)
            (await ReadIdsAsync(scoped, table)).Should().BeEmpty($"another organization sees none of {table}");

        // Writes: as org X, a row of its own goes in ...
        await SetOrganizationAsync(scoped, OrgX);
        await ExecuteAsync(scoped, InsertLedgerSql, ("id", Guid.NewGuid()), ("org", OrgX), ("order", "ord-1"));
        await ExecuteAsync(scoped, InsertPostingSql, ("id", Guid.NewGuid()), ("order", "ord-2"), ("org", OrgX));
        // ... but none under org Y's name.
        var ledgerUnderOthersName = async () => await ExecuteAsync(scoped, InsertLedgerSql, ("id", Guid.NewGuid()), ("org", OrgY), ("order", "ord-1"));
        var catalogUnderOthersName = async () => await ExecuteAsync(scoped, InsertCatalogSql, ("id", Guid.NewGuid()), ("org", OrgY), ("code", "ITEM-FORGED"));
        var postingUnderOthersName = async () => await ExecuteAsync(scoped, InsertPostingSql, ("id", Guid.NewGuid()), ("order", "ord-2"), ("org", OrgY));
        var pavingUnderOthersName = async () => await ExecuteAsync(scoped, InsertPavingSql, ("id", Guid.NewGuid()), ("parent", "ord-1"), ("child", "ord-2"), ("org", OrgY));
        var manholeUnderOthersName = async () => await ExecuteAsync(scoped, InsertManholeSql, ("id", Guid.NewGuid()), ("conduit", "c-forged"), ("org", OrgY));
        foreach (var rejected in new[] { ledgerUnderOthersName, catalogUnderOthersName, postingUnderOthersName, pavingUnderOthersName, manholeUnderOthersName })
            (await rejected.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        foreach (var table in Tables)
        {
            (await ExecuteAsync(scoped, $"""DELETE FROM field_service.{Quote(table)} WHERE "{OwnerColumn(table)}" = '{OrgY}'""")).Should().Be(0, $"another organization's rows of {table} are not even deletable");
            (await ExecuteAsync(scoped, $"""UPDATE field_service.{Quote(table)} SET "{OwnerColumn(table)}" = "{OwnerColumn(table)}" WHERE "{OwnerColumn(table)}" = '{OrgY}'""")).Should().Be(0);
        }

        // The owner's view: only X's two new rows were added.
        (await QueryAsync(db, CountSql(FieldServiceSchema, Ledger))).Should().Equal("4");
        (await QueryAsync(db, CountSql(FieldServiceSchema, Postings))).Should().Equal("4");
        foreach (var table in new[] { Catalog, Paving, Manholes })
            (await QueryAsync(db, CountSql(FieldServiceSchema, table))).Should().Equal(string.Equals(table, Paving, StringComparison.Ordinal) ? "2" : "3");
    }

    // ── The models and their snapshots ────────────────────────────────────────

    /// <summary>
    /// On a database both chains migrated: neither context reports pending model changes (the shipped snapshots are the
    /// models), the kernel model — and its shipped snapshot — hold none of the five entity types, and the vertical's model
    /// holds exactly them.
    /// </summary>
    public static async Task AssertTheKernelModelHasNoFieldServiceEntitiesAndBothModelsMatchTheirSnapshotsAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await db.Database.MigrateAsync();
        await using var fieldService = NewFieldService(db);
        await fieldService.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await fieldService.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        db.Database.HasPendingModelChanges().Should().BeFalse("the kernel model and its shipped snapshot agree");
        fieldService.Database.HasPendingModelChanges().Should().BeFalse("the FieldService model and its shipped snapshot agree");

        db.Model.GetEntityTypes().Select(e => e.GetTableName()).Should().NotIntersectWith(Tables);
        db.Model.GetEntityTypes().Select(e => e.ClrType.Namespace ?? string.Empty)
            .Should().NotContain(ns => ns.StartsWith("GeoAssets.FieldService", StringComparison.Ordinal));
        db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model.GetEntityTypes().Select(e => e.GetTableName())
            .Should().NotIntersectWith(Tables, "the kernel's shipped snapshot was regenerated without them");
        fieldService.Model.GetEntityTypes().Select(e => e.GetTableName()).Should().BeEquivalentTo(Tables);
        fieldService.Model.GetEntityTypes().Select(e => e.GetSchema()).Should().OnlyContain(schema => string.Equals(schema, FieldServiceSchema, StringComparison.Ordinal));
        fieldService.GetService<IMigrationsAssembly>().ModelSnapshot!.Model.GetEntityTypes().Select(e => e.GetTableName())
            .Should().BeEquivalentTo(Tables);
    }

    /// <summary>
    /// The FieldService history's own kernel migration <c>RemoveFieldServiceTablesFromKernelModel</c>, up and down, over a
    /// database holding data in the five tables, changes nothing in the database: counts, checksums, shape and the set of
    /// relations are identical. (The Server history's counterpart drops the tables — its own tests.)
    /// </summary>
    public static async Task AssertTheKernelModelMigrationIsANoOpOverDataAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = chain.Single(id => id.EndsWith("_" + FieldServiceModelMigrationName, StringComparison.Ordinal));
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().EndWith("_" + OccupancyMigrationName, "the migration that carries the model change follows XD01-380's");
        chain.Skip(chain.IndexOf(migration) + 1).Should().OnlyContain(
            later => later.EndsWith("_AddCrossOrganizationSyncMirrors", StringComparison.Ordinal),
            "nothing in the kernel chain follows it but XD01-328's sync mirrors, which leave these tables alone");

        await migrator.MigrateAsync(previous);
        await SeedAsync(db);
        var beforeTables = await SnapshotAsync(db, "public");
        var beforeRelations = await RelationsAsync(db);

        await migrator.MigrateAsync(migration);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(migration);
        (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(beforeTables);
        (await RelationsAsync(db)).Should().Equal(beforeRelations, "Up created and dropped nothing");

        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
        (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(beforeTables);
        (await RelationsAsync(db)).Should().Equal(beforeRelations, "Down created and dropped nothing");

        await db.Database.MigrateAsync();
        (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(beforeTables);
    }

    // ── Seed and snapshot ─────────────────────────────────────────────────────

    private const string InsertOrderSql =
        """
        INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
        VALUES (@id, 'Order', '', 'inspection', 'Draft', 0, 'tester', @org, now(), '{}', '[]')
        """;

    private const string InsertLedgerSql =
        """
        INSERT INTO field_service."ContractorUsLedgerEntries" ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
        VALUES (@id, @org, @order, 'US-1', 'Poste', 1, 1, '[]', now())
        """;

    private const string InsertCatalogSql =
        """
        INSERT INTO field_service."UsCatalogItems" ("Id", "OrganizationId", "ItemCode", "Description", "Unit", "BaseUsFactor", "RegionalMultiplier", "MonetaryConversionRate", "EffectiveFrom")
        VALUES (@id, @org, @code, '', 'un', 1, 1, 1, now())
        """;

    private const string InsertPostingSql =
        """
        INSERT INTO field_service.field_contractor_us_postings ("Id", "ServiceOrderId", "ContractorOrganizationId", "ItemCode", "Quantity", "UnitUs", "TotalUs", "MonetaryRate", "TotalBrl", "PostedAtUtc")
        VALUES (@id, @order, @org, 'ITEM-1', 1, 1, 1, 1, 1, now())
        """;

    private const string InsertPavingSql =
        """
        INSERT INTO field_service.field_icerp_paving_measurements ("Id", "ParentServiceOrderId", "ChildServiceOrderId", "OrganizationId", "LengthMeters", "WidthMeters", "DepthMeters", "RecordedAreaSquareMeters", "ActualAreaSquareMeters", "VolumeCubicMeters", "RecordedAtUtc")
        VALUES (@id, @parent, @child, @org, 1, 1, 1, 1, 1, 1, now())
        """;

    private const string InsertManholeSql =
        """
        INSERT INTO field_service.field_manhole_audits ("Id", "ConduitFeatureId", "OrganizationId", "BoreOccupancyPercentage", "SubDuctsJson", "SpliceEnclosureCondition", "Nr33ChecklistAttributesJson", "AuditedBy", "AuditedAtUtc")
        VALUES (@id, @conduit, @org, 10, '[]', 'ok', '{}', 'tester', now())
        """;

    /// <summary>
    /// Three orders (two of org X, one of org Y) and, in <c>public</c> — written the way the kernel chain's own migrations'
    /// tables take them, as the table owner — three ledger entries, three catalog items, three postings, two paving
    /// measurements and three manhole audits across the two organizations, with values that would not survive a lossy copy
    /// (fractions at the column scales, non-ASCII text, JSON).
    /// </summary>
    public static async Task SeedAsync(ServiceOrderDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-1"), ("org", OrgX));
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-2"), ("org", OrgX));
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-3"), ("org", OrgY));

        await ExecuteAsync(db, $$"""
            INSERT INTO public."ContractorUsLedgerEntries" ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt", "UpdatedAt") VALUES
                ('a1000000-0000-4000-8000-000000000001', '{{OrgX}}', 'ord-1', 'US-001', 'Poste trocado — São Paulo', 2.5, 150.5025, '[{"url":"https://example.com/p1.jpg"}]', '2026-11-01 10:00:00+00', NULL),
                ('a1000000-0000-4000-8000-000000000002', '{{OrgX}}', NULL, 'US-002', 'Cabo', 0.0001, 99999999.9999, '[]', '2026-11-02 10:00:00+00', '2026-11-03 10:00:00+00'),
                ('a1000000-0000-4000-8000-000000000009', '{{OrgY}}', 'ord-3', 'US-009', 'Crossarm', 1, 1, '[]', '2026-11-03 10:00:00+00', NULL)
            """);
        await ExecuteAsync(db, $$"""
            INSERT INTO public."UsCatalogItems" ("Id", "OrganizationId", "ItemCode", "Description", "Unit", "BaseUsFactor", "RegionalMultiplier", "MonetaryConversionRate", "EffectiveFrom", "EffectiveTo") VALUES
                ('b1000000-0000-4000-8000-000000000001', '{{OrgX}}', 'US-EL-014', 'Poste de concreto', 'un', 2.500000, 1.200000, 85.123456, '2026-01-01 00:00:00+00', '2026-06-30 00:00:00+00'),
                ('b1000000-0000-4000-8000-000000000002', '{{OrgX}}', 'US-EL-014', 'Poste de concreto (v2)', 'un', 2.750000, 1.0, 90, '2026-07-01 00:00:00+00', NULL),
                ('b1000000-0000-4000-8000-000000000009', '{{OrgY}}', 'US-EL-014', 'Poste', 'un', 1, 1, 1, '2026-01-01 00:00:00+00', NULL)
            """);
        await ExecuteAsync(db, $$"""
            INSERT INTO public.field_contractor_us_postings ("Id", "ServiceOrderId", "ContractorOrganizationId", "ItemCode", "Quantity", "UnitUs", "TotalUs", "MonetaryRate", "TotalBrl", "PostedAtUtc") VALUES
                ('c1000000-0000-4000-8000-000000000001', 'ord-1', '{{OrgX}}', 'US-EL-014', 10.5000, 3.000000, 31.500000, 85.000000, 2677.50, '2026-11-01 12:00:00+00'),
                ('c1000000-0000-4000-8000-000000000002', 'ord-2', '{{OrgX}}', 'US-EL-014', 1, 1, 1, 85, 85.00, '2026-11-02 12:00:00+00'),
                ('c1000000-0000-4000-8000-000000000009', 'ord-3', '{{OrgY}}', 'US-EL-014', 2, 2, 4, 85, 340.00, '2026-11-03 12:00:00+00')
            """);
        await ExecuteAsync(db, $$"""
            INSERT INTO public.field_icerp_paving_measurements ("Id", "ParentServiceOrderId", "ChildServiceOrderId", "OrganizationId", "LengthMeters", "WidthMeters", "DepthMeters", "RecordedAreaSquareMeters", "ActualAreaSquareMeters", "VolumeCubicMeters", "RecordedAtUtc") VALUES
                ('d1000000-0000-4000-8000-000000000001', 'ord-1', 'ord-2', '{{OrgX}}', 2.5, 1.25, 0.5, 3.125, 3.2, 1.5625, '2026-11-01 13:00:00+00'),
                ('d1000000-0000-4000-8000-000000000009', 'ord-3', 'ord-3', '{{OrgY}}', 1, 1, 1, 1, 1, 1, '2026-11-03 13:00:00+00')
            """);
        await ExecuteAsync(db, $$"""
            INSERT INTO public.field_manhole_audits ("Id", "ConduitFeatureId", "OrganizationId", "BoreOccupancyPercentage", "SubDuctsJson", "SpliceEnclosureCondition", "Nr33ChecklistAttributesJson", "AuditedBy", "AuditedAtUtc") VALUES
                ('e1000000-0000-4000-8000-000000000001', 'conduit-1', '{{OrgX}}', 12.5, '[{"id":1}]', 'Bom', '{"nr33":true}', 'crew-1', '2026-11-01 14:00:00+00'),
                ('e1000000-0000-4000-8000-000000000002', 'conduit-1', '{{OrgX}}', 75.25, '[]', 'Corroído', '{}', 'crew-2', '2026-11-02 14:00:00+00'),
                ('e1000000-0000-4000-8000-000000000009', 'conduit-9', '{{OrgY}}', 0, '[]', 'ok', '{}', 'crew-9', '2026-11-03 14:00:00+00')
            """);
    }

    /// <summary>
    /// Per table of <paramref name="schema"/>: a row count with a checksum of every row's text, the row-level-security flags,
    /// policies, columns, indexes and constraints — schema names removed, so the two schemas compare equal.
    /// </summary>
    public static async Task<Dictionary<string, string>> SnapshotAsync(ServiceOrderDbContext db, string schema)
    {
        ArgumentNullException.ThrowIfNull(db);

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in Tables)
        {
            var qualified = $"{schema}.\"{table}\"";
            var parts = new List<string>
            {
                "rows " + string.Join(';', await QueryAsync(db, CountSql(schema, table))),
                "checksum " + string.Join(';', await QueryAsync(db, ChecksumSql(schema, table))),
                "security " + string.Join(';', await QueryAsync(db, RowSecuritySql(schema, table))),
            };
            parts.AddRange((await QueryAsync(db, $"""
                SELECT (policyname || '|' || permissive || '|' || cmd || '|' || qual || '|' || coalesce(with_check, '-'))::text AS "Value"
                FROM pg_policies WHERE schemaname = '{schema}' AND tablename = '{table}' ORDER BY policyname COLLATE "C"
                """)).Select(p => "policy " + p));
            parts.AddRange((await QueryAsync(db, $"""
                SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(character_maximum_length::text, '-') || ':' || coalesce(collation_name, '-')
                        || ':' || coalesce(column_default, '-')
                        || ':' || CASE WHEN data_type = 'numeric' THEN numeric_precision::text || ',' || numeric_scale::text ELSE '-' END)::text AS "Value"
                FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = '{table}' ORDER BY ordinal_position
                """)).Select(c => "column " + c));
            parts.AddRange((await QueryAsync(db, $"""
                SELECT replace(replace(indexdef, 'public.', ''), 'field_service.', '')::text AS "Value"
                FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = '{table}' ORDER BY indexname COLLATE "C"
                """)).Select(i => "index " + i));
            parts.AddRange((await QueryAsync(db, $"""
                SELECT (conname || ' ' || replace(replace(pg_get_constraintdef(oid), 'public.', ''), 'field_service.', ''))::text AS "Value"
                FROM pg_constraint WHERE conrelid = '{qualified}'::regclass ORDER BY conname COLLATE "C"
                """)).Select(c => "constraint " + c));
            snapshot[table] = string.Join('\n', parts);
        }

        return snapshot;
    }

    /// <summary>The snapshot of one table without its row-level-security flags and policies.</summary>
    public static string WithoutSecurity(string snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return string.Join('\n', snapshot.Split('\n').Where(line =>
            !line.StartsWith("security ", StringComparison.Ordinal) && !line.StartsWith("policy ", StringComparison.Ordinal)));
    }

    public static Task<List<string>> RelationsAsync(ServiceOrderDbContext db) =>
        QueryAsync(db, """
            SELECT (n.nspname || '.' || c.relname || ':' || c.relkind::text)::text AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('public', 'field_service') ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C"
            """);

    public static string CountSql(string schema, string table) => $"""SELECT count(*)::text AS "Value" FROM {schema}."{table}" """;

    public static string ChecksumSql(string schema, string table) =>
        $"""SELECT coalesce(md5(string_agg(t::text, ',' ORDER BY t."Id"::text)), '-')::text AS "Value" FROM {schema}."{table}" t""";

    public static string RowSecuritySql(string schema, string table) =>
        $"""SELECT (relrowsecurity::text || ':' || relforcerowsecurity::text)::text AS "Value" FROM pg_class WHERE oid = '{schema}."{table}"'::regclass""";

    public static string PolicyCountSql(string schema, string table) =>
        $"""SELECT count(*)::text AS "Value" FROM pg_policies WHERE schemaname = '{schema}' AND tablename = '{table}' AND policyname = '{PolicyName}'""";

    public static string IndexNamesSql(string schema, string table) =>
        $"""SELECT indexname::text AS "Value" FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = '{table}' ORDER BY indexname COLLATE "C" """;

    /// <summary>The Server history never gave the postings row-level security; the baseline does, so their security lines legitimately differ.</summary>
    private static bool IsUnsecuredPostings(string table, bool postingsHadPolicy) =>
        string.Equals(table, Postings, StringComparison.Ordinal) && !postingsHadPolicy;

    private static int CountLines(string snapshot, string prefix) =>
        snapshot.Split('\n').Count(line => line.StartsWith(prefix, StringComparison.Ordinal));

    private static string ForeignKeysSql(string table) =>
        $"""SELECT (conname || ' ' || replace(pg_get_constraintdef(oid), 'field_service.', ''))::text AS "Value" FROM pg_constraint WHERE conrelid = 'field_service."{table}"'::regclass AND contype = 'f' ORDER BY conname COLLATE "C" """;

    public static async Task<bool> ExistsAsync(ServiceOrderDbContext db, string schema, string table) =>
        string.Equals((await QueryAsync(db, $"""SELECT (to_regclass('{schema}."{table}"') IS NOT NULL)::text AS "Value" """)).Single(), "true", StringComparison.Ordinal);

    private static string OwnerColumn(string table) => string.Equals(table, Postings, StringComparison.Ordinal) ? "ContractorOrganizationId" : "OrganizationId";

    private static string Quote(string table) => table is Ledger or Catalog ? $"\"{table}\"" : table;

    // ── Plumbing ──────────────────────────────────────────────────────────────

    public static FieldServiceDbContext NewFieldService(ServiceOrderDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return new(new DbContextOptionsBuilder<FieldServiceDbContext>()
            .UseNpgsql(db.Database.GetConnectionString(), FieldServiceDbContext.ConfigureMigrations)
            .Options);
    }

    /// <summary>
    /// Migrates the kernel chain up to, and not including, its migration of XD01-381 — <c>RemoveFieldServiceTablesFromKernelModel</c>
    /// in the FieldService history, <c>DropFieldServiceTablesFromKernel</c> in the Server one: the last state in which the
    /// tables are in <c>public</c> in both.
    /// </summary>
    public static Task MigrateKernelToTheLastMigrationHoldingTheTablesAsync(ServiceOrderDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.GetService<IMigrator>().MigrateAsync(KernelMigrationBeforeXD01381(db));
    }

    /// <summary>The id of the kernel migration just before XD01-381's in the history of <paramref name="db"/>.</summary>
    public static string KernelMigrationBeforeXD01381(ServiceOrderDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        var chain = db.Database.GetMigrations().ToList();
        var own = chain.Single(id =>
            id.EndsWith("_" + FieldServiceModelMigrationName, StringComparison.Ordinal)
            || id.EndsWith("_" + ServerDropMigrationName, StringComparison.Ordinal));
        var previous = chain[chain.IndexOf(own) - 1];
        previous.Should().EndWith("_" + OccupancyMigrationName, "XD01-381's kernel migration follows XD01-380's");
        return previous;
    }

    public static Task<List<string>> QueryAsync(ServiceOrderDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    public static async Task ExecuteAsync(ServiceOrderDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(db);

        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql, parameters);
    }

    public static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var batch = new NpgsqlBatch(connection);
        var command = new NpgsqlBatchCommand(sql);
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new NpgsqlParameter(name, value));
        batch.BatchCommands.Add(command);
        return await batch.ExecuteNonQueryAsync();
    }

    private static Task<int> SetOrganizationAsync(NpgsqlConnection connection, Guid organizationId) =>
        ExecuteAsync(connection, "SELECT set_config('app.current_organization_id', @orgId, false)", ("orgId", organizationId.ToString()));

    private static async Task<List<string>> ReadIdsAsync(NpgsqlConnection connection, string table)
    {
        await using var batch = new NpgsqlBatch(connection);
        batch.BatchCommands.Add(new NpgsqlBatchCommand($"""SELECT "Id"::text FROM field_service.{Quote(table)} ORDER BY "Id"::text COLLATE "C" """));
        var ids = new List<string>();
        await using var reader = await batch.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>
    /// A pooling-free connection as a non-owner role holding exactly the privileges the moved tables' policies need: the five
    /// tables of <c>field_service</c> (their policies read nothing else).
    /// </summary>
    private static async Task<NpgsqlConnection> OpenScopedRoleAsync(ServiceOrderDbContext db)
    {
        await RoleLock.WaitAsync();
        try
        {
            await using var owner = new NpgsqlConnection(db.Database.GetConnectionString());
            await owner.OpenAsync();
            await using var setup = owner.CreateCommand();
            setup.CommandText = $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ScopedRole}') THEN
                        CREATE ROLE {ScopedRole} LOGIN PASSWORD '{ScopedRole}';
                    END IF;
                END $$;
                GRANT USAGE ON SCHEMA field_service TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON field_service."ContractorUsLedgerEntries" TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON field_service."UsCatalogItems" TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON field_service.field_contractor_us_postings TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON field_service.field_icerp_paving_measurements TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON field_service.field_manhole_audits TO {ScopedRole};
                """;
            await setup.ExecuteNonQueryAsync();
        }
        finally
        {
            RoleLock.Release();
        }

        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = ScopedRole,
            Password = ScopedRole,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
