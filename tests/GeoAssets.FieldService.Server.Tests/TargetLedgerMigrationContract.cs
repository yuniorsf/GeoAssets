using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GeoAssets.Workflow.Persistence.Contracts;

/// <summary>
/// What the <c>AddTargetLedgerVertical</c> migration (XD01-379) must do to <c>"ServiceOrderTargets"</c> on PostgreSQL,
/// whichever migration history carries it. One body, compiled into both suites that own a history for
/// <see cref="ServiceOrderDbContext"/> — this project for the isolated FieldService database, and GeoAssets.Security.Tests
/// (which links this file) for the shared GeoAssets.Server one — so both histories are held to the same assertions.
///
/// Each member takes a factory of contexts on a freshly named, not yet migrated database and a function opening a
/// connection as the table owner; the row-level-security member also takes one opening a connection as a non-owner
/// role that holds the table privileges the policies need.
/// </summary>
internal static class TargetLedgerMigrationContract
{
    public const string MigrationName = "AddTargetLedgerVertical";

    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000379");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000379");

    private const string ColumnsSql =
        """
        SELECT (column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(character_maximum_length::text, '-') || ':' || coalesce(column_default, '-'))::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'ServiceOrderTargets'
          AND column_name IN ('VerticalId', 'KernelOrderId', 'OrderTypeId', 'Exclusivity')
        ORDER BY column_name COLLATE "C"
        """;

    private const string IndexesSql =
        """
        SELECT indexname::text AS "Value" FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'ServiceOrderTargets'
        ORDER BY indexname COLLATE "C"
        """;

    private const string UniqueIndexSql =
        """
        SELECT indexdef::text AS "Value" FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'ServiceOrderTargets' AND indexdef LIKE 'CREATE UNIQUE INDEX "IX_%'
        """;

    private const string ForeignKeysSql =
        """
        SELECT (conname || ' ' || pg_get_constraintdef(oid))::text AS "Value" FROM pg_constraint
        WHERE conrelid = '"ServiceOrderTargets"'::regclass AND contype = 'f'
        """;

    private const string PolicyNamesSql =
        """
        SELECT policyname::text AS "Value" FROM pg_policies
        WHERE schemaname = 'public' AND tablename = 'ServiceOrderTargets'
        ORDER BY policyname COLLATE "C"
        """;

    private const string TriggerSql =
        """
        SELECT tgname::text AS "Value" FROM pg_trigger
        WHERE tgrelid = '"ServiceOrderTargets"'::regclass AND NOT tgisinternal
        """;

    private const string InsertOrderSql =
        """
        INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
        VALUES (@id, 'Order', '', 'inspection', 'Draft', 0, 'tester', @org, now(), '{}', '[]')
        """;

    private static string InsertTargetSql(bool withLedgerColumns) =>
        withLedgerColumns
            ? """
              INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "VerticalId", "KernelOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
              VALUES (@id, @order, @vertical, @kernelOrder, @feature, 'pole', '[]', 'Pending', 0, now(), 'tester', @org, 0)
              """
            : """
              INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
              VALUES (@id, @order, @feature, 'pole', '[]', 'Pending', 0, now(), 'tester', @org, 0)
              """;

    /// <summary>Up over existing in-process rows: they are backfilled, and the new shape is exactly the documented one.</summary>
    public static async Task AssertUpBackfillsExistingRowsAndReshapesTheTableAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        var migrator = db.GetService<IMigrator>();
        var (migration, previous) = Ids(db);

        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, ColumnsSql)).Should().BeEmpty();
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(false), ("id", "tgt-1"), ("order", "ord-1"), ("feature", "feat-1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(false), ("id", "tgt-2"), ("order", "ord-1"), ("feature", "feat-2"), ("org", OrgX));

        await migrator.MigrateAsync(migration);

        (await QueryAsync(db, """SELECT ("Id" || ':' || "VerticalId" || ':' || "KernelOrderId" || ':' || coalesce("OrderTypeId", '-') || ':' || coalesce("Exclusivity"::text, '-'))::text AS "Value" FROM "ServiceOrderTargets" ORDER BY "Id" """))
            .Should().Equal("tgt-1:kernel:ord-1:-:-", "tgt-2:kernel:ord-1:-:-");
        (await QueryAsync(db, ColumnsSql)).Should().Equal(
            "Exclusivity:integer:YES:-:-",
            "KernelOrderId:character varying:YES:36:-",
            "OrderTypeId:character varying:YES:64:-",
            "VerticalId:character varying:NO:64:'kernel'::character varying");
        (await QueryAsync(db, UniqueIndexSql)).Should().ContainSingle().Which.Should()
            .Contain("(\"VerticalId\", \"ServiceOrderId\", \"FeatureId\")").And.NotContain("IX_ServiceOrderTargets_ServiceOrderId_FeatureId");
        (await QueryAsync(db, IndexesSql)).Should().Contain("IX_ServiceOrderTargets_KernelOrderId")
            .And.NotContain("IX_ServiceOrderTargets_ServiceOrderId_FeatureId");
        (await QueryAsync(db, ForeignKeysSql)).Should().Equal(
            """FK_ServiceOrderTargets_ServiceOrders_KernelOrderId FOREIGN KEY ("KernelOrderId") REFERENCES "ServiceOrders"("Id") ON DELETE CASCADE""");
        (await QueryAsync(db, PolicyNamesSql)).Should().Contain(["ledger_vertical_isolation_policy", "ledger_vertical_tenant_policy"]);
        (await QueryAsync(db, TriggerSql)).Should().Equal("service_order_targets_kernel_order_id");
        (await QueryAsync(db, """SELECT (conname || ' ' || pg_get_constraintdef(oid))::text AS "Value" FROM pg_constraint WHERE conrelid = '"ServiceOrderTargets"'::regclass AND contype = 'c'"""))
            .Should().ContainSingle().Which.Should().StartWith("CK_ServiceOrderTargets_KernelOrderId CHECK");

        // Deleting the order still deletes its in-process targets, through the moved foreign key.
        await ExecuteAsync(db, """DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-1'""");
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().BeEmpty();
    }

    /// <summary>The trigger files a kernel row under its order and a remote row under none; the check constraint backs it when the trigger is off.</summary>
    public static async Task AssertTriggerDerivesKernelOrderIdAndCheckConstraintRejectsMismatchesAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        await db.Database.MigrateAsync();
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-1"), ("org", OrgX));

        // A raw kernel insert that names no KernelOrderId — as a writer that predates the column does — gets it set.
        await ExecuteAsync(db, InsertTargetSql(false), ("id", "k-1"), ("order", "ord-1"), ("feature", "f1"), ("org", OrgX));
        // A kernel insert naming the wrong one, and a remote insert naming one, are corrected by the trigger.
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "k-2"), ("order", "ord-1"), ("vertical", "kernel"), ("kernelOrder", "wrong"), ("feature", "f2"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "r-1"), ("order", "ord-1"), ("vertical", "isp"), ("kernelOrder", "ord-1"), ("feature", "f3"), ("org", OrgX));

        (await QueryAsync(db, """SELECT ("Id" || ':' || "VerticalId" || ':' || coalesce("KernelOrderId", 'NULL'))::text AS "Value" FROM "ServiceOrderTargets" ORDER BY "Id" """))
            .Should().Equal("k-1:kernel:ord-1", "k-2:kernel:ord-1", "r-1:isp:NULL");

        // With the trigger off the check constraint is all that stands: it rejects both mismatches.
        await ExecuteAsync(db, """ALTER TABLE "ServiceOrderTargets" DISABLE TRIGGER service_order_targets_kernel_order_id""");
        var remoteWithKernelOrder = async () => await ExecuteAsync(db, InsertTargetSql(true), ("id", "r-2"), ("order", "ord-1"), ("vertical", "isp"), ("kernelOrder", "ord-1"), ("feature", "f4"), ("org", OrgX));
        var kernelWithoutKernelOrder = async () => await ExecuteAsync(db, InsertTargetSql(true), ("id", "k-3"), ("order", "ord-1"), ("vertical", "kernel"), ("kernelOrder", DBNull.Value), ("feature", "f5"), ("org", OrgX));
        var kernelWithOtherKernelOrder = async () => await ExecuteAsync(db, InsertTargetSql(true), ("id", "k-4"), ("order", "ord-1"), ("vertical", "kernel"), ("kernelOrder", "wrong"), ("feature", "f6"), ("org", OrgX));
        foreach (var insert in new[] { remoteWithKernelOrder, kernelWithoutKernelOrder, kernelWithOtherKernelOrder })
            (await insert.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("CK_ServiceOrderTargets_KernelOrderId");
        await ExecuteAsync(db, """ALTER TABLE "ServiceOrderTargets" ENABLE TRIGGER service_order_targets_kernel_order_id""");

        // A remote vertical's target needs no order row at all, and two verticals may share an order id and a feature.
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "r-3"), ("order", "no-such-order"), ("vertical", "isp"), ("kernelOrder", DBNull.Value), ("feature", "f1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "r-4"), ("order", "no-such-order"), ("vertical", "other"), ("kernelOrder", DBNull.Value), ("feature", "f1"), ("org", OrgX));
        var duplicate = async () => await ExecuteAsync(db, InsertTargetSql(true), ("id", "r-5"), ("order", "no-such-order"), ("vertical", "isp"), ("kernelOrder", DBNull.Value), ("feature", "f1"), ("org", OrgX));
        (await duplicate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        // A kernel target of an order that does not exist is refused by the moved foreign key.
        var orphan = async () => await ExecuteAsync(db, InsertTargetSql(false), ("id", "k-9"), ("order", "no-such-order"), ("feature", "f9"), ("org", OrgX));
        (await orphan.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    /// <summary>Down refuses, changing nothing, while a remote vertical's target exists; with kernel rows only it restores the previous shape.</summary>
    public static async Task AssertDownRefusesWhileRemoteRowsExistAndOtherwiseRestoresThePreviousShapeAsync(Func<Task<ServiceOrderDbContext>> newEmptyDatabase)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        await using var db = await newEmptyDatabase();
        var migrator = db.GetService<IMigrator>();
        var (migration, previous) = Ids(db);
        await migrator.MigrateAsync(migration);
        await ExecuteAsync(db, InsertOrderSql, ("id", "ord-1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "k-1"), ("order", "ord-1"), ("vertical", "kernel"), ("kernelOrder", "ord-1"), ("feature", "f1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "r-1"), ("order", "ord-1"), ("vertical", "isp"), ("kernelOrder", DBNull.Value), ("feature", "f1"), ("org", OrgX));

        var down = async () => await migrator.MigrateAsync(previous);

        (await down.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("Cannot revert AddTargetLedgerVertical").And.Contain("1 target(s)");
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(migration, "the refused way back left the migration applied");
        (await QueryAsync(db, """SELECT ("Id" || ':' || "VerticalId")::text AS "Value" FROM "ServiceOrderTargets" ORDER BY "Id" """)).Should().Equal("k-1:kernel", "r-1:isp");
        (await QueryAsync(db, ColumnsSql)).Should().HaveCount(4);
        (await QueryAsync(db, PolicyNamesSql)).Should().Contain("ledger_vertical_isolation_policy");
        (await QueryAsync(db, TriggerSql)).Should().Equal("service_order_targets_kernel_order_id");

        // With kernel rows only, the same way back goes through and restores the previous shape.
        await ExecuteAsync(db, """DELETE FROM "ServiceOrderTargets" WHERE "VerticalId" <> 'kernel'""");
        await migrator.MigrateAsync(previous);

        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(migration);
        (await QueryAsync(db, ColumnsSql)).Should().BeEmpty();
        (await QueryAsync(db, UniqueIndexSql)).Should().ContainSingle().Which.Should().Contain("(\"ServiceOrderId\", \"FeatureId\")");
        (await QueryAsync(db, IndexesSql)).Should().NotContain("IX_ServiceOrderTargets_KernelOrderId");
        (await QueryAsync(db, ForeignKeysSql)).Should().Equal(
            """FK_ServiceOrderTargets_ServiceOrders_ServiceOrderId FOREIGN KEY ("ServiceOrderId") REFERENCES "ServiceOrders"("Id") ON DELETE CASCADE""");
        (await QueryAsync(db, PolicyNamesSql)).Should().NotContain(["ledger_vertical_isolation_policy", "ledger_vertical_tenant_policy"]);
        (await QueryAsync(db, TriggerSql)).Should().BeEmpty();
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM pg_proc WHERE proname = 'service_order_targets_set_kernel_order_id'""")).Should().Equal("0");
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("k-1");
        (await QueryAsync(db, """SELECT count(*)::text AS "Value" FROM pg_constraint WHERE conrelid = '"ServiceOrderTargets"'::regclass AND contype = 'c'""")).Should().Equal("0");

        // Up again over the kept row.
        await migrator.MigrateAsync(migration);
        (await QueryAsync(db, """SELECT ("Id" || ':' || "VerticalId" || ':' || "KernelOrderId")::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("k-1:kernel:ord-1");
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Row-level security of the ledger's remote rows as a non-owner role: visible and writable only by their own organization's
    /// session; in-process rows behave as before; and a remote row never rides an in-process order's policy.
    /// </summary>
    public static async Task AssertRemoteRowsAreScopedToTheirOrganizationAsync(
        Func<Task<ServiceOrderDbContext>> newEmptyDatabase,
        Func<ServiceOrderDbContext, Task<NpgsqlConnection>> openScopedRole)
    {
        ArgumentNullException.ThrowIfNull(newEmptyDatabase);
        ArgumentNullException.ThrowIfNull(openScopedRole);
        await using var db = await newEmptyDatabase();
        await db.Database.MigrateAsync();
        await ExecuteAsync(db, InsertOrderSql, ("id", "same"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "kernel-x"), ("order", "same"), ("vertical", "kernel"), ("kernelOrder", "same"), ("feature", "f1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "remote-x"), ("order", "same"), ("vertical", "isp"), ("kernelOrder", DBNull.Value), ("feature", "f1"), ("org", OrgX));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "remote-y"), ("order", "same"), ("vertical", "isp"), ("kernelOrder", DBNull.Value), ("feature", "f2"), ("org", OrgY));
        await ExecuteAsync(db, InsertTargetSql(true), ("id", "remote-y2"), ("order", "other"), ("vertical", "other"), ("kernelOrder", DBNull.Value), ("feature", "f3"), ("org", OrgY));

        await using var scoped = await openScopedRole(db);

        (await ReadAsync(scoped)).Should().BeEmpty("a session that named no organization sees nothing");
        (await VisibleAsync(scoped, OrgX)).Should().Equal("kernel-x", "remote-x");
        (await VisibleAsync(scoped, OrgY)).Should().Equal("remote-y", "remote-y2");
        (await VisibleAsync(scoped, Guid.NewGuid())).Should().BeEmpty();

        // Writes: a session inserts remote rows only under its own organization, and touches only its own organization's.
        await SetOrganizationAsync(scoped, OrgY);
        var underOtherOrg = async () => await ExecuteAsync(scoped, InsertTargetSql(true), ("id", "forged"), ("order", "o9"), ("vertical", "isp"), ("kernelOrder", DBNull.Value), ("feature", "f9"), ("org", OrgX));
        (await underOtherOrg.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await ExecuteAsync(scoped, InsertTargetSql(true), ("id", "remote-y3"), ("order", "o9"), ("vertical", "isp"), ("kernelOrder", DBNull.Value), ("feature", "f9"), ("org", OrgY));
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrderTargets" SET "ReleasedAt" = now() WHERE "ServiceOrderId" = 'same'""")).Should().Be(1, "only org Y's own remote row of that order id");
        (await ExecuteAsync(scoped, """DELETE FROM "ServiceOrderTargets" WHERE "Id" IN ('remote-x', 'kernel-x')""")).Should().Be(0);
        var handedOver = async () => await ExecuteAsync(scoped, """UPDATE "ServiceOrderTargets" SET "OrganizationId" = @other WHERE "Id" = 'remote-y3'""", ("other", OrgX));
        (await handedOver.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // The owner's view is unchanged by all of it: nothing of org X's was touched.
        (await QueryAsync(db, $$"""SELECT ("Id" || ':' || ("ReleasedAt" IS NULL)::text)::text AS "Value" FROM "ServiceOrderTargets" WHERE "OrganizationId" = '{{OrgX}}' ORDER BY "Id" """))
            .Should().Equal("kernel-x:true", "remote-x:true");
    }

    private static (string Migration, string Previous) Ids(ServiceOrderDbContext db)
    {
        var chain = db.Database.GetMigrations().ToList();
        var migration = chain.Single(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        return (migration, chain[chain.IndexOf(migration) - 1]);
    }

    private static Task<List<string>> QueryAsync(ServiceOrderDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    private static async Task ExecuteAsync(ServiceOrderDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
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

    private static async Task<List<string>> VisibleAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SetOrganizationAsync(connection, organizationId);
        return await ReadAsync(connection);
    }

    private static async Task<List<string>> ReadAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ServiceOrderTargets" ORDER BY "Id" COLLATE "C" """;
        var ids = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }
}
