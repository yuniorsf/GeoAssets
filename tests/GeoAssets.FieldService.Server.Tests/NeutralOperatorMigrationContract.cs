using FluentAssertions;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GeoAssets.Workflow.Persistence.Contracts;

/// <summary>
/// What moving the pole occupancy tables out of the kernel's <see cref="ServiceOrderDbContext"/> (XD01-380) must do to a
/// PostgreSQL database, whichever kernel migration history it carries: the kernel's <c>RemoveOccupancyFromKernelModel</c>
/// changes nothing, and the NeutralOperator chain's baseline (<c>AdoptOccupancyTables</c>) moves
/// <c>field_pole_occupancies</c>, <c>field_pole_occupancy_entries</c> and <c>"ServiceOrderOccupancyChangeSets"</c> from
/// <c>public</c> into <c>neutral_operator</c> with their rows, indexes, constraints, row-level-security flag and policies —
/// and refuses or creates in the cases it was written to. One body, compiled into both suites that own a history for
/// <see cref="ServiceOrderDbContext"/> — GeoAssets.FieldService.Server.Tests (the isolated FieldService database) and
/// GeoAssets.Security.Tests (which links this file; the shared GeoAssets.Server one) — so both are held to the same assertions.
///
/// Each member takes a factory of kernel contexts on a freshly named, not yet migrated database.
/// </summary>
internal static class NeutralOperatorMigrationContract
{
    public const string BaselineName = "AdoptOccupancyTables";
    public const string KernelMigrationName = "RemoveOccupancyFromKernelModel";
    public const string LedgerMigrationName = "AddTargetLedgerVertical";

    /// <summary>
    /// The kernel migrations that follow <see cref="KernelMigrationName"/> and have nothing to do with the occupancy tables:
    /// XD01-381's, one per history — the Server chain drops the FieldService tables, the FieldService chain only changes its model.
    /// </summary>
    private static readonly string[] LaterKernelMigrationNames = ["DropFieldServiceTablesFromKernel", "RemoveFieldServiceTablesFromKernelModel"];

    private const string Poles = "field_pole_occupancies";
    private const string Entries = "field_pole_occupancy_entries";
    private const string ChangeSets = "ServiceOrderOccupancyChangeSets";
    private const string NeutralOperatorSchema = "neutral_operator";
    private const string ScopedRole = "neutral_operator_contract_scoped_role";
    private const string PolicyName = "tenant_isolation_policy";

    private static readonly string[] Tables = [Poles, Entries, ChangeSets];
    private static readonly SemaphoreSlim RoleLock = new(1, 1);

    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000380");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000380");

    private static readonly Guid PoleX1 = new("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid PoleX2 = new("a1000000-0000-4000-8000-000000000002");
    private static readonly Guid PoleY9 = new("a1000000-0000-4000-8000-000000000009");

    // ── The moved path ────────────────────────────────────────────────────────

    /// <summary>
    /// Rows, content, indexes, constraints, the row-level-security flags and the policies of all three tables are identical
    /// after the move — and the change sets still cascade from the kernel's targets, across the schemas.
    /// </summary>
    public static async Task AssertTheMoveKeepsRowsContentIndexesConstraintsAndPoliciesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToAsync(db, LedgerMigrationName);
        await SeedAsync(db);
        var before = await SnapshotAsync(db, "public");
        before[Poles].Should().StartWith("rows 3\n");
        before[Entries].Should().StartWith("rows 5\n");
        before[ChangeSets].Should().StartWith("rows 5\n");

        await db.Database.MigrateAsync();
        (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(before, "the kernel's head migration touches nothing");
        await using var neutralOperator = NewNeutralOperator(db);
        await MigrateToBaselineAsync(neutralOperator);

        foreach (var table in Tables)
            (await ExistsAsync(db, "public", table)).Should().BeFalse($"{table} left public");
        var after = await SnapshotAsync(db, NeutralOperatorSchema);
        after.Should().BeEquivalentTo(before, "the tables moved with every row, index, constraint, flag and policy");
        foreach (var table in Tables)
        {
            after[table].Should().Contain($"{PolicyName}|PERMISSIVE|ALL|");
            (await QueryAsync(db, PolicyCountSql(NeutralOperatorSchema, table))).Should().Equal("1");
            (await QueryAsync(db, RowSecuritySql(NeutralOperatorSchema, table))).Should().Equal(["true:false"], "enabled, not forced — as before");
        }

        (await QueryAsync(db, ForeignKeysSql(ChangeSets))).Should().Equal(
            """FK_ServiceOrderOccupancyChangeSets_ServiceOrderTargets_TargetId FOREIGN KEY ("TargetId") REFERENCES "ServiceOrderTargets"("Id") ON DELETE CASCADE""");
        (await QueryAsync(db, ForeignKeysSql(Entries))).Should().ContainSingle().Which.Should().Contain("ON DELETE CASCADE");
        (await QueryAsync(db, """SELECT "MigrationId"::text AS "Value" FROM neutral_operator."__EFMigrationsHistory_neutral_operator" """))
            .Should().ContainSingle().Which.Should().EndWith("_" + BaselineName);
        (await neutralOperator.Database.GetPendingMigrationsAsync()).Should().NotContain(id => id.EndsWith("_" + BaselineName, StringComparison.Ordinal),
            "the baseline is applied; whatever the chain adds after it is not this contract's");
        neutralOperator.Database.HasPendingModelChanges().Should().BeFalse("the shipped snapshot is the model");

        // Deleting an order still removes its targets and, through the foreign key across the schemas, their change sets.
        await ExecuteAsync(db, """DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-1'""");
        (await QueryAsync(db, CountSql(NeutralOperatorSchema, ChangeSets))).Should().Equal(["2"], "only ord-2's and ord-3's sets remain");
        (await QueryAsync(db, CountSql(NeutralOperatorSchema, Poles))).Should().Equal("3");
        await ExecuteAsync(db, $"""DELETE FROM neutral_operator.field_pole_occupancies WHERE "Id" = '{PoleX1}'""");
        (await QueryAsync(db, CountSql(NeutralOperatorSchema, Entries))).Should().Equal(["2"], "a pole takes its entries with it");
    }

    // ── Baseline: idempotency and guards ──────────────────────────────────────

    /// <summary>Running the baseline again (its history row deleted) is a no-op: same counts, content, policies.</summary>
    public static async Task AssertReapplyingTheBaselineIsANoOpAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToAsync(db, LedgerMigrationName);
        await SeedAsync(db);
        await db.Database.MigrateAsync();
        await using var neutralOperator = NewNeutralOperator(db);
        await MigrateToBaselineAsync(neutralOperator);
        var applied = await SnapshotAsync(db, NeutralOperatorSchema);

        await ExecuteAsync(db, """DELETE FROM neutral_operator."__EFMigrationsHistory_neutral_operator" """);
        (await neutralOperator.Database.GetPendingMigrationsAsync()).Should().ContainSingle(id => id.EndsWith("_" + BaselineName, StringComparison.Ordinal));
        await MigrateToBaselineAsync(neutralOperator);

        (await SnapshotAsync(db, NeutralOperatorSchema)).Should().BeEquivalentTo(applied);
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM neutral_operator."__EFMigrationsHistory_neutral_operator" """)).Should().Equal("1");
        foreach (var table in Tables)
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
    }

    /// <summary>A same-named table already in <c>neutral_operator</c> while the real one is in <c>public</c>: the baseline raises and nothing changes.</summary>
    public static async Task AssertAStrayTableInBothSchemasRaisesAndChangesNothingAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToAsync(db, LedgerMigrationName);
        await SeedAsync(db);
        await db.Database.MigrateAsync();
        var before = await SnapshotAsync(db, "public");
        // The third table of the baseline's loop: the first two are moved before it is reached, so the whole step must roll back.
        await ExecuteAsync(db, $"""CREATE SCHEMA IF NOT EXISTS neutral_operator; CREATE TABLE neutral_operator."{ChangeSets}" (stray integer); INSERT INTO neutral_operator."{ChangeSets}" VALUES (7);""");
        await using var neutralOperator = NewNeutralOperator(db);

        var migrate = () => neutralOperator.Database.MigrateAsync();

        (await migrate.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("exists in both public and neutral_operator").And.Contain(ChangeSets);
        (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(before, "the real tables stayed where they were, rows included");
        (await ExistsAsync(db, NeutralOperatorSchema, Poles)).Should().BeFalse("the move of the first table was rolled back with the step");
        (await ExistsAsync(db, NeutralOperatorSchema, Entries)).Should().BeFalse();
        (await QueryAsync(db, $"""SELECT stray::text AS "Value" FROM neutral_operator."{ChangeSets}" """)).Should().Equal(["7"], "the stray table is untouched");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM neutral_operator."__EFMigrationsHistory_neutral_operator" """)).Should().Equal("0");
    }

    /// <summary>The NeutralOperator chain on a database the kernel chain never ran on: refuses, creates nothing.</summary>
    public static async Task AssertTheChainWithoutTheKernelChainRaisesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await using var neutralOperator = NewNeutralOperator(db);

        var migrate = () => neutralOperator.Database.MigrateAsync();

        (await migrate.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should()
            .Contain("ServiceOrderTargets").And.Contain("kernel ServiceOrderDbContext migrations");
        foreach (var table in Tables)
        {
            (await ExistsAsync(db, NeutralOperatorSchema, table)).Should().BeFalse();
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
        }

        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM neutral_operator."__EFMigrationsHistory_neutral_operator" """)).Should().Equal("0");
    }

    /// <summary>A fresh database: the full kernel chain, then the baseline — empty tables in <c>neutral_operator</c> with row-level security and policies.</summary>
    public static async Task AssertAFreshDatabaseGetsEmptyTablesWithRowLevelSecurityAndPoliciesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await db.Database.MigrateAsync();
        await using var neutralOperator = NewNeutralOperator(db);

        await MigrateToBaselineAsync(neutralOperator);

        var shape = await SnapshotAsync(db, NeutralOperatorSchema);
        foreach (var table in Tables)
        {
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
            (await QueryAsync(db, CountSql(NeutralOperatorSchema, table))).Should().Equal("0");
            (await QueryAsync(db, RowSecuritySql(NeutralOperatorSchema, table))).Should().Equal("true:false");
            (await QueryAsync(db, PolicyCountSql(NeutralOperatorSchema, table))).Should().Equal("1");
            shape[table].Should().Contain($"{PolicyName}|PERMISSIVE|ALL|");
        }

        (await QueryAsync(db, IndexNamesSql(NeutralOperatorSchema, Poles))).Should().Equal(
            "AK_field_pole_occupancies_Id_OrganizationId", "IX_field_pole_occupancies_OrganizationId_PoleFeatureId",
            "IX_field_pole_occupancies_PoleFeatureId", "PK_field_pole_occupancies");
        (await QueryAsync(db, IndexNamesSql(NeutralOperatorSchema, Entries))).Should().Equal(
            "IX_field_pole_occupancy_entries_OccupantOrganizationId", "IX_field_pole_occupancy_entries_OrganizationId",
            "IX_field_pole_occupancy_entries_PoleOccupancyId_OrganizationId", "IX_field_pole_occupancy_entries_PoleOccupancyId_SlotIndex",
            "PK_field_pole_occupancy_entries");
        (await QueryAsync(db, IndexNamesSql(NeutralOperatorSchema, ChangeSets))).Should().Equal(
            "IX_ServiceOrderOccupancyChangeSets_OrganizationId", "IX_ServiceOrderOccupancyChangeSets_ServiceOrderId_Status",
            "IX_ServiceOrderOccupancyChangeSets_TargetId", "IX_ServiceOrderOccupancyChangeSets_TargetId_Staged",
            "PK_ServiceOrderOccupancyChangeSets");
    }

    /// <summary>Tables in neither schema (the kernel's copies dropped) with the kernel's targets present: created, in the same shape as the moved ones.</summary>
    public static async Task AssertTheCreatePathBuildsTheSameShapeAsTheMovedPathAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var moved = await newEmptyDatabase();
        await moved.Database.MigrateAsync();
        await using var movedNeutralOperator = NewNeutralOperator(moved);
        await MigrateToBaselineAsync(movedNeutralOperator);
        var movedShape = await SnapshotAsync(moved, NeutralOperatorSchema);

        await using var created = await newEmptyDatabase();
        await created.Database.MigrateAsync();
        await ExecuteAsync(created, $"""DROP TABLE public."{ChangeSets}"; DROP TABLE public.{Entries}; DROP TABLE public.{Poles};""");
        foreach (var table in Tables)
            (await ExistsAsync(created, "public", table)).Should().BeFalse();
        await using var createdNeutralOperator = NewNeutralOperator(created);

        await MigrateToBaselineAsync(createdNeutralOperator);

        (await SnapshotAsync(created, NeutralOperatorSchema)).Should().BeEquivalentTo(movedShape);
        foreach (var table in Tables)
            (await ExistsAsync(created, "public", table)).Should().BeFalse("the create path builds in neutral_operator only");
    }

    // ── Down ──────────────────────────────────────────────────────────────────

    /// <summary>Down moves the tables back to <c>public</c> with data, flags and policies; up again restores.</summary>
    public static async Task AssertDownMovesTheTablesBackWithTheirDataAndUpRestoresThemAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToAsync(db, LedgerMigrationName);
        await SeedAsync(db);
        var original = await SnapshotAsync(db, "public");
        await db.Database.MigrateAsync();
        await using var neutralOperator = NewNeutralOperator(db);
        await MigrateToBaselineAsync(neutralOperator);
        var moved = await SnapshotAsync(db, NeutralOperatorSchema);

        await neutralOperator.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);

        (await neutralOperator.Database.GetAppliedMigrationsAsync()).Should().BeEmpty();
        foreach (var table in Tables)
            (await ExistsAsync(db, NeutralOperatorSchema, table)).Should().BeFalse($"{table} went back");
        (await SnapshotAsync(db, "public")).Should().BeEquivalentTo(original, "rows, indexes, constraints, flags and policies came back with the tables");

        await MigrateToBaselineAsync(neutralOperator);

        (await SnapshotAsync(db, NeutralOperatorSchema)).Should().BeEquivalentTo(moved);
        foreach (var table in Tables)
            (await ExistsAsync(db, "public", table)).Should().BeFalse();
    }

    /// <summary>Down refuses (changing nothing) while <c>public</c> holds a same-named table; with it gone, it goes through.</summary>
    public static async Task AssertDownRefusesWhilePublicHoldsASameNamedTableAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToAsync(db, LedgerMigrationName);
        await SeedAsync(db);
        await db.Database.MigrateAsync();
        await using var neutralOperator = NewNeutralOperator(db);
        await MigrateToBaselineAsync(neutralOperator);
        var moved = await SnapshotAsync(db, NeutralOperatorSchema);
        // The last table of the Down loop's order is the poles: the other two are moved before it is reached.
        await ExecuteAsync(db, $"CREATE TABLE public.{Poles} (stray integer)");

        var down = () => neutralOperator.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("exists in both public and neutral_operator").And.Contain(Poles);
        (await SnapshotAsync(db, NeutralOperatorSchema)).Should().BeEquivalentTo(moved, "the whole step rolled back, data included");
        (await ExistsAsync(db, "public", ChangeSets)).Should().BeFalse("the earlier moves of the step were rolled back");
        (await ExistsAsync(db, "public", Entries)).Should().BeFalse();
        (await neutralOperator.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();

        await ExecuteAsync(db, $"DROP TABLE public.{Poles}");
        await neutralOperator.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        (await ExistsAsync(db, "public", Poles)).Should().BeTrue();
    }

    // ── Row-level security after the move ─────────────────────────────────────

    /// <summary>
    /// As a non-owner role on the moved tables: the owning organization sees its rows, another sees none, and a change set
    /// against another organization's target — or one that does not agree with its own target — is rejected by the policy's
    /// WITH CHECK, which resolves the kernel's table across the schemas.
    /// </summary>
    public static async Task AssertRowLevelSecurityHoldsOnTheMovedTablesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await MigrateKernelToAsync(db, LedgerMigrationName);
        await SeedAsync(db);
        await db.Database.MigrateAsync();
        await using var neutralOperator = NewNeutralOperator(db);
        await MigrateToBaselineAsync(neutralOperator);
        await using var scoped = await OpenScopedRoleAsync(db);

        (await ReadIdsAsync(scoped, Poles)).Should().BeEmpty("a session that named no organization sees nothing");
        (await ReadIdsAsync(scoped, ChangeSets)).Should().BeEmpty();

        await SetOrganizationAsync(scoped, OrgX);
        (await ReadIdsAsync(scoped, Poles)).Should().Equal(PoleX1.ToString(), PoleX2.ToString());
        (await ReadIdsAsync(scoped, Entries)).Should().HaveCount(4);
        (await ReadIdsAsync(scoped, ChangeSets)).Should().HaveCount(4, "org X's t1 (staged, discarded), t2 and t3 sets");

        await SetOrganizationAsync(scoped, OrgY);
        (await ReadIdsAsync(scoped, Poles)).Should().Equal(PoleY9.ToString());
        (await ReadIdsAsync(scoped, Entries)).Should().HaveCount(1);
        (await ReadIdsAsync(scoped, ChangeSets)).Should().HaveCount(1);

        await SetOrganizationAsync(scoped, Guid.NewGuid());
        foreach (var table in Tables)
            (await ReadIdsAsync(scoped, table)).Should().BeEmpty("another organization sees none of it");

        // Writes: as org X, a set on X's own target (t2 has no staged set) goes in across the schemas ...
        await SetOrganizationAsync(scoped, OrgX);
        await ExecuteAsync(scoped, InsertChangeSetSql, ("id", Guid.NewGuid()), ("order", "ord-1"), ("target", "t2"), ("status", "Staged"), ("org", OrgX));
        // ... but not against org Y's target, not under org Y's name, and not with an order that is not the target's own.
        var againstOthersTarget = async () => await ExecuteAsync(scoped, InsertChangeSetSql, ("id", Guid.NewGuid()), ("order", "ord-3"), ("target", "t4"), ("status", "Discarded"), ("org", OrgX));
        var underOthersName = async () => await ExecuteAsync(scoped, InsertChangeSetSql, ("id", Guid.NewGuid()), ("order", "ord-1"), ("target", "t2"), ("status", "Discarded"), ("org", OrgY));
        var withOthersOrder = async () => await ExecuteAsync(scoped, InsertChangeSetSql, ("id", Guid.NewGuid()), ("order", "ord-2"), ("target", "t2"), ("status", "Discarded"), ("org", OrgX));
        foreach (var rejected in new[] { againstOthersTarget, underOthersName, withOthersOrder })
            (await rejected.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // Poles: only under its own organization.
        var poleUnderOthersName = async () => await ExecuteAsync(scoped, InsertPoleSql, ("id", Guid.NewGuid()), ("pole", "forged"), ("org", OrgY));
        (await poleUnderOthersName.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await ExecuteAsync(scoped, InsertPoleSql, ("id", Guid.NewGuid()), ("pole", "own-new-pole"), ("org", OrgX));
        (await ExecuteAsync(scoped, $"""DELETE FROM neutral_operator.{Poles} WHERE "OrganizationId" = '{OrgY}'""")).Should().Be(0, "another organization's poles are not even deletable");

        // The owner's view: only X's one new set and pole were added.
        (await QueryAsync(db, CountSql(NeutralOperatorSchema, ChangeSets))).Should().Equal("6");
        (await QueryAsync(db, CountSql(NeutralOperatorSchema, Poles))).Should().Equal("4");
    }

    // ── The kernel's own migration ────────────────────────────────────────────

    /// <summary>
    /// <c>RemoveOccupancyFromKernelModel</c>, up and down, over a database holding occupancy data, changes nothing in the
    /// database; and the kernel model, and its shipped snapshot, hold no occupancy entity type.
    /// </summary>
    public static async Task AssertTheKernelMigrationIsANoOpAndTheKernelModelHasNoOccupancyAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = chain.Single(id => id.EndsWith("_" + KernelMigrationName, StringComparison.Ordinal));
        var previous = chain[chain.IndexOf(migration) - 1];
        previous.Should().EndWith("_" + LedgerMigrationName, "the migration that carries the model change follows the ledger migration");
        chain.Skip(chain.IndexOf(migration) + 1).Should().OnlyContain(
            id => LaterKernelMigrationNames.Any(later => id.EndsWith("_" + later, StringComparison.Ordinal)),
            "nothing but the later kernel migrations that leave the occupancy tables alone (XD01-381 removed the FieldService tables, not these) follows it");

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

        var occupancyTables = new[] { Poles, Entries, ChangeSets };
        db.Model.GetEntityTypes().Select(e => e.GetTableName()).Should().NotIntersectWith(occupancyTables);
        db.Model.GetEntityTypes().Select(e => e.ClrType.Namespace ?? string.Empty).Should().NotContain(ns => ns.StartsWith("GeoAssets.NeutralOperator", StringComparison.Ordinal));
        db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model.GetEntityTypes().Select(e => e.GetTableName())
            .Should().NotIntersectWith(occupancyTables, "the shipped snapshot was regenerated without them");
        db.Database.HasPendingModelChanges().Should().BeFalse("the kernel model and its snapshot agree");
    }

    // ── Seed and snapshot ─────────────────────────────────────────────────────

    private const string InsertOrderSql =
        """
        INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
        VALUES (@id, 'Order', '', 'inspection', 'Draft', 0, 'tester', @org, now(), '{}', '[]')
        """;

    private const string InsertTargetSql =
        """
        INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
        VALUES (@id, @order, @feature, 'pole', '[]', 'Pending', 0, now(), 'tester', @org, 0)
        """;

    private const string InsertPoleSql =
        """
        INSERT INTO neutral_operator.field_pole_occupancies ("Id", "PoleFeatureId", "OrganizationId", "Version", "UpdatedAt")
        VALUES (@id, @pole, @org, 1, now())
        """;

    private const string InsertChangeSetSql =
        """
        INSERT INTO neutral_operator."ServiceOrderOccupancyChangeSets"
            ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
        VALUES (@id, @order, @target, 'pole-1', '[]', 0, @status, 'tester', now(), NULL, @org)
        """;

    /// <summary>
    /// Three orders (two of org X, one of org Y) with four targets, three poles with five entries and five change sets,
    /// all in <c>public</c> — written the way the kernel chain's own migrations' tables take them, as the table owner.
    /// </summary>
    private static async Task SeedAsync(ServiceOrderDbContext db)
    {
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-1"), ("org", OrgX));
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-2"), ("org", OrgX));
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-3"), ("org", OrgY));
        await ExecuteAsync(db, InsertTargetSql, ("id", "t1"), ("order", "ord-1"), ("feature", "pole-1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql, ("id", "t2"), ("order", "ord-1"), ("feature", "pole-2"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql, ("id", "t3"), ("order", "ord-2"), ("feature", "pole-1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql, ("id", "t4"), ("order", "ord-3"), ("feature", "pole-9"), ("org", OrgY));

        await ExecuteAsync(db, $"""
            INSERT INTO public.field_pole_occupancies ("Id", "PoleFeatureId", "OrganizationId", "Version", "UpdatedAt") VALUES
                ('{PoleX1}', 'pole-1', '{OrgX}', 3, '2026-11-01 10:00:00+00'),
                ('{PoleX2}', 'pole-2', '{OrgX}', 1, '2026-11-02 10:00:00+00'),
                ('{PoleY9}', 'pole-9', '{OrgY}', 1, '2026-11-03 10:00:00+00');
            INSERT INTO public.field_pole_occupancy_entries ("Id", "PoleOccupancyId", "SlotIndex", "Status", "CableDiameterMm", "CableLoadDaN", "OrganizationId") VALUES
                ('b1000000-0000-4000-8000-000000000001', '{PoleX1}', 1, 'AuthorizedContracted', 12.5, 80, '{OrgX}'),
                ('b1000000-0000-4000-8000-000000000002', '{PoleX1}', 2, 'ActiveReservation', 9.25, 0, '{OrgX}'),
                ('b1000000-0000-4000-8000-000000000003', '{PoleX1}', NULL, 'AbandonedDeadDrop', 7.125, 1.5, '{OrgX}'),
                ('b1000000-0000-4000-8000-000000000004', '{PoleX2}', 3, 'AuthorizedContracted', 10, 20, '{OrgX}'),
                ('b1000000-0000-4000-8000-000000000009', '{PoleY9}', 1, 'AuthorizedContracted', 11, 30, '{OrgY}');
            INSERT INTO public."ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId") VALUES
                ('c1000000-0000-4000-8000-000000000001', 'ord-1', 't1', 'pole-1', '[]', 3, 'Staged', 'crew-1', '2026-11-04 10:00:00+00', NULL, '{OrgX}'),
                ('c1000000-0000-4000-8000-000000000002', 'ord-1', 't1', 'pole-1', '[]', 2, 'Discarded', 'crew-1', '2026-11-03 10:00:00+00', '2026-11-03 11:00:00+00', '{OrgX}'),
                ('c1000000-0000-4000-8000-000000000003', 'ord-1', 't2', 'pole-2', '[]', 1, 'Committed', 'crew-2', '2026-11-03 10:00:00+00', '2026-11-03 12:00:00+00', '{OrgX}'),
                ('c1000000-0000-4000-8000-000000000004', 'ord-2', 't3', 'pole-1', '[]', 3, 'Staged', 'crew-2', '2026-11-05 10:00:00+00', NULL, '{OrgX}'),
                ('c1000000-0000-4000-8000-000000000009', 'ord-3', 't4', 'pole-9', '[]', 1, 'Staged', 'crew-9', '2026-11-05 11:00:00+00', NULL, '{OrgY}');
            """);
    }

    /// <summary>
    /// Per table of <paramref name="schema"/>: a row count with a checksum of every row's text, the row-level-security flags,
    /// policies, columns, indexes and constraints — schema names removed, so the two schemas compare equal.
    /// </summary>
    private static async Task<Dictionary<string, string>> SnapshotAsync(ServiceOrderDbContext db, string schema)
    {
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
                        || ':' || CASE WHEN data_type = 'numeric' THEN numeric_precision::text || ',' || numeric_scale::text ELSE '-' END)::text AS "Value"
                FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = '{table}' ORDER BY ordinal_position
                """)).Select(c => "column " + c));
            parts.AddRange((await QueryAsync(db, $"""
                SELECT replace(replace(indexdef, 'public.', ''), 'neutral_operator.', '')::text AS "Value"
                FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = '{table}' ORDER BY indexname COLLATE "C"
                """)).Select(i => "index " + i));
            parts.AddRange((await QueryAsync(db, $"""
                SELECT (conname || ' ' || replace(replace(pg_get_constraintdef(oid), 'public.', ''), 'neutral_operator.', ''))::text AS "Value"
                FROM pg_constraint WHERE conrelid = '{qualified}'::regclass ORDER BY conname COLLATE "C"
                """)).Select(c => "constraint " + c));
            snapshot[table] = string.Join('\n', parts);
        }

        return snapshot;
    }

    private static Task<List<string>> RelationsAsync(ServiceOrderDbContext db) =>
        QueryAsync(db, """
            SELECT (n.nspname || '.' || c.relname || ':' || c.relkind::text)::text AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('public', 'neutral_operator') ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C"
            """);

    private static string CountSql(string schema, string table) => $"""SELECT count(*)::text AS "Value" FROM {schema}."{table}" """;

    private static string ChecksumSql(string schema, string table) =>
        $"""SELECT coalesce(md5(string_agg(t::text, ',' ORDER BY t."Id"::text)), '-')::text AS "Value" FROM {schema}."{table}" t""";

    private static string RowSecuritySql(string schema, string table) =>
        $"""SELECT (relrowsecurity::text || ':' || relforcerowsecurity::text)::text AS "Value" FROM pg_class WHERE oid = '{schema}."{table}"'::regclass""";

    private static string PolicyCountSql(string schema, string table) =>
        $"""SELECT count(*)::text AS "Value" FROM pg_policies WHERE schemaname = '{schema}' AND tablename = '{table}' AND policyname = '{PolicyName}'""";

    private static string IndexNamesSql(string schema, string table) =>
        $"""SELECT indexname::text AS "Value" FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = '{table}' ORDER BY indexname COLLATE "C" """;

    private static string ForeignKeysSql(string table) =>
        $"""SELECT (conname || ' ' || replace(pg_get_constraintdef(oid), 'neutral_operator.', ''))::text AS "Value" FROM pg_constraint WHERE conrelid = 'neutral_operator."{table}"'::regclass AND contype = 'f' ORDER BY conname COLLATE "C" """;

    private static async Task<bool> ExistsAsync(ServiceOrderDbContext db, string schema, string table) =>
        string.Equals((await QueryAsync(db, $"""SELECT (to_regclass('{schema}."{table}"') IS NOT NULL)::text AS "Value" """)).Single(), "true", StringComparison.Ordinal);

    // ── Plumbing ──────────────────────────────────────────────────────────────

    private static NeutralOperatorDbContext NewNeutralOperator(ServiceOrderDbContext db) =>
        new(new DbContextOptionsBuilder<NeutralOperatorDbContext>()
            .UseNpgsql(db.Database.GetConnectionString(), NeutralOperatorDbContext.ConfigureMigrations)
            .Options);

    /// <summary>
    /// The NeutralOperator chain up to and including its baseline — what this contract is about. The migrations the chain
    /// gained after it (XD01-383's <c>AddOccupancyApplySagas</c>) have a contract of their own.
    /// </summary>
    private static Task MigrateToBaselineAsync(NeutralOperatorDbContext neutralOperator)
    {
        var target = neutralOperator.Database.GetMigrations().Single(id => id.EndsWith("_" + BaselineName, StringComparison.Ordinal));
        return neutralOperator.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task MigrateKernelToAsync(ServiceOrderDbContext db, string migrationName)
    {
        var target = db.Database.GetMigrations().Single(id => id.EndsWith("_" + migrationName, StringComparison.Ordinal));
        return db.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<List<string>> QueryAsync(ServiceOrderDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    private static async Task ExecuteAsync(ServiceOrderDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        if (parameters.Length == 0)
        {
            // A script of several statements: only the simple protocol takes it.
            await db.Database.ExecuteSqlRawAsync(sql);
            return;
        }

        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql, parameters);
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
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
        var sql = table switch
        {
            Poles => """SELECT "Id"::text FROM neutral_operator.field_pole_occupancies ORDER BY "Id"::text COLLATE "C" """,
            Entries => """SELECT "Id"::text FROM neutral_operator.field_pole_occupancy_entries ORDER BY "Id"::text COLLATE "C" """,
            ChangeSets => """SELECT "Id"::text FROM neutral_operator."ServiceOrderOccupancyChangeSets" ORDER BY "Id"::text COLLATE "C" """,
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not one of the three moved tables."),
        };
        await using var batch = new NpgsqlBatch(connection);
        batch.BatchCommands.Add(new NpgsqlBatchCommand(sql));
        var ids = new List<string>();
        await using var reader = await batch.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>
    /// A pooling-free connection as a non-owner role holding exactly the privileges the moved tables' policies need: the
    /// three tables, the kernel's targets and orders the change set policy reads, and the grant table those policies read.
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
                GRANT USAGE ON SCHEMA neutral_operator TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON neutral_operator.field_pole_occupancies TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON neutral_operator.field_pole_occupancy_entries TO {ScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON neutral_operator."ServiceOrderOccupancyChangeSets" TO {ScopedRole};
                GRANT SELECT ON "ServiceOrderTargets" TO {ScopedRole};
                GRANT SELECT ON "ServiceOrders" TO {ScopedRole};
                DO $$
                BEGIN
                    IF to_regclass('public."OrganizationGrants"') IS NOT NULL THEN
                        GRANT SELECT ON "OrganizationGrants" TO {ScopedRole};
                    END IF;
                    IF to_regclass('public."OrganizationGrantMirror"') IS NOT NULL THEN
                        GRANT SELECT ON "OrganizationGrantMirror" TO {ScopedRole};
                    END IF;
                END $$;
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
