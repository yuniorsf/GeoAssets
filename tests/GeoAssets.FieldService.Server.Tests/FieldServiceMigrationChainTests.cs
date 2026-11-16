using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Applies the <b>real</b> <see cref="ServiceOrderDbContext"/> migration chain carried by
/// <c>GeoAssets.FieldService.Server</c> to an empty PostgreSQL database and proves the XD01-304 migration
/// (<c>AddOrderTypeTargetPolicyAndAttributesSchema</c>: nullable <c>text</c> columns
/// <c>"OrderTypes"."TargetPolicyJson"</c> and <c>"OrderTypes"."AttributesSchemaJson"</c>) applies
/// on top of its predecessor, keeps existing rows, and rolls back cleanly.
///
/// <c>GeoAssets.FieldService.Server</c> is one of the two hosts that carry a migration history for
/// this context (the isolated-database one, XD01-290; <c>GeoAssets.Server</c> carries the
/// shared-database one, covered by <c>ServiceOrderMigrationChainTests</c> in
/// GeoAssets.Security.Tests). The same is proven for the XD01-306 pair
/// <c>AddServiceOrderTargets</c> / <c>AddServiceOrderTargetsRowLevelSecurity</c>, whose policy in
/// this history reads <c>"OrganizationGrantMirror"</c> and is therefore exercised here, as a
/// non-owner role, rather than in the shared-database security suite. Same container pattern
/// as <c>IpBillingMigrationChainTests</c>: one lazily-started Postgres shared by the facts, each migrating its own freshly-named database;
/// the container is torn down with the test process.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string MigrationsAssemblyName = "GeoAssets.FieldService.Server";
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

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;

    [Fact]
    public async Task MigrateAsync_OnEmptyPostgres_AppliesEveryMigrationInTheChain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        var chain = db.Database.GetMigrations().ToList();
        chain.Should().Contain(id => id.EndsWith("_" + MigrationName, StringComparison.Ordinal));
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(chain);
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
    private const string ScopedRole = "fieldservice_target_scoped_role";

    private static readonly Guid OwnerOrganization = Guid.Parse("c1c1c1c1-0000-0000-0000-000000000001");
    private static readonly Guid GranteeOrganization = Guid.Parse("c2c2c2c2-0000-0000-0000-000000000002");
    private static readonly Guid StrangerOrganization = Guid.Parse("c3c3c3c3-0000-0000-0000-000000000003");

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

    private const string TargetsTableExistsSql =
        """SELECT (to_regclass('public."ServiceOrderTargets"') IS NOT NULL)::text AS "Value" """;

    // This history has no trigger filling "ServiceOrders"."RowVersion" (the shared database's
    // comes from a raw-SQL migration this history does not carry), so a raw insert supplies it.
    private const string InsertOrderSql =
        """
        INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson", "RowVersion")
        VALUES (@id, @id, '', 'inspection', 'Draft', 0, 'tester', @orgId, now(), @attributesJson, '[]', E'\\x01')
        """;

    private const string InsertTargetSql =
        """
        INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
        VALUES (@id, @orderId, @featureId, 'pole', '[]', 'Pending', 0, now(), 'tester', @orgId, 0)
        """;

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
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal("false:false:0");
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
        await using var connection = await OpenAsync(db);
        await InsertOrderAsync(connection, "ord-1", OwnerOrganization);

        // Up: table, then policy.
        await migrator.MigrateAsync(table);
        await InsertTargetAsync(connection, "tgt-1", "ord-1", OwnerOrganization);
        await migrator.MigrateAsync(rowLevelSecurity);
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal(["true:false:1"], "enabled, not forced, exactly one policy");

        // Down, one step: the policy goes, the table and its rows stay.
        await migrator.MigrateAsync(table);
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal("false:false:0");
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("tgt-1");

        // Down, second step: the table goes, the order stays.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, TargetsTableExistsSql)).Should().Equal("false");
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain([table, rowLevelSecurity]);
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrders" """)).Should().Equal("ord-1");

        // Up again.
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await QueryAsync(db, TargetRowLevelSecuritySql)).Should().Equal("true:false:1");
    }

    [Fact]
    public async Task Model_HasNoChangesTheMigrationChainDoesNotAlreadyContain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "a model change without a migration would leave this host's database behind the code");
    }

    /// <summary>
    /// Every way a grant can fail to apply, plus the two that do: the targets of the owner's
    /// order are visible to the grantee exactly when an active, unexpired, matching mirror row
    /// exists — and the stranger's targets never are.
    /// </summary>
    [Theory]
    [InlineData(true, false, null, null, true)]
    [InlineData(false, false, null, null, false)]
    [InlineData(true, true, null, null, false)]
    [InlineData(true, false, "AssetLayer", null, false)]
    [InlineData(true, false, "ServiceOrder", null, true)]
    [InlineData(true, false, null, "district-7", true)]
    [InlineData(true, false, null, "district-9", false)]
    public async Task ServiceOrderTargetsPolicy_AsANonOwnerRole_IsolatesByOrganization_AndFollowsTheMirroredGrant(
        bool grantIsActive, bool grantIsExpired, string? grantResourceType, string? grantScopeId, bool expectGranteeSeesOwnersTargets)
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-owner", OwnerOrganization, """{"scopeId":"district-7"}""");
            await InsertOrderAsync(owner, "ord-stranger", StrangerOrganization);
            await InsertTargetAsync(owner, "tgt-owner", "ord-owner", OwnerOrganization);
            await InsertTargetAsync(owner, "tgt-stranger", "ord-stranger", StrangerOrganization);
            await InsertMirroredGrantAsync(owner, grantIsActive, grantIsExpired, grantResourceType, grantScopeId);
        }

        await using var scoped = await OpenAsScopedRoleAsync(db);

        (await VisibleTargetIdsAsync(scoped, OwnerOrganization)).Should().Equal("tgt-owner");
        (await VisibleTargetIdsAsync(scoped, StrangerOrganization)).Should().Equal("tgt-stranger");
        (await VisibleTargetIdsAsync(scoped, GranteeOrganization)).Should().Equal(
            expectGranteeSeesOwnersTargets ? ["tgt-owner"] : []);
    }

    [Fact]
    public async Task ServiceOrderTargetsPolicy_AsANonOwnerRole_RejectsWritesOutsideAnOrderTheOrganizationOwnsOrIsGranted()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-owner", OwnerOrganization);
            await InsertTargetAsync(owner, "tgt-owner", "ord-owner", OwnerOrganization);
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsScopedRoleAsync(db);

        // A stranger: no insert under the owner's order, whichever organization it claims; no update; no delete.
        await SetOrganizationAsync(scoped, StrangerOrganization);
        var forgedAsOwner = async () => await InsertTargetAsync(scoped, "tgt-forged-1", "ord-owner", OwnerOrganization, "feat-2");
        var forgedAsItself = async () => await InsertTargetAsync(scoped, "tgt-forged-2", "ord-owner", StrangerOrganization, "feat-3");
        await forgedAsOwner.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        await forgedAsItself.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        (await ReleaseTargetsAsync(scoped, "ord-owner")).Should().Be(0);
        (await DeleteTargetAsync(scoped, "tgt-owner")).Should().Be(0);

        // The grantee: may add to and release the granted order's targets, but only under the order's organization.
        await SetOrganizationAsync(scoped, GranteeOrganization);
        var underItsOwnOrganization = async () => await InsertTargetAsync(scoped, "tgt-grantee-own", "ord-owner", GranteeOrganization, "feat-4");
        await underItsOwnOrganization.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        await InsertTargetAsync(scoped, "tgt-grantee-added", "ord-owner", OwnerOrganization, "feat-5");
        (await ReleaseTargetsAsync(scoped, "ord-owner")).Should().Be(2);

        // The owner: its own order, its own organization — and it cannot hand a target to another organization.
        await SetOrganizationAsync(scoped, OwnerOrganization);
        await InsertTargetAsync(scoped, "tgt-owner-2", "ord-owner", OwnerOrganization, "feat-6");
        var mislabelled = async () => await InsertTargetAsync(scoped, "tgt-mislabelled", "ord-owner", StrangerOrganization, "feat-7");
        await mislabelled.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        (await DeleteTargetAsync(scoped, "tgt-owner-2")).Should().Be(1);
    }

    private static string MigrationId(ServiceOrderDbContext db, string name) =>
        db.Database.GetMigrations().Single(id => id.EndsWith("_" + name, StringComparison.Ordinal));

    private static async Task<NpgsqlConnection> OpenAsync(ServiceOrderDbContext db)
    {
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Row-level security does not restrict a table's owner (no FORCE, as everywhere in this
    /// repo), so the policy is only observable as a second, non-owner role — with exactly the
    /// privileges the migration's comment says an application role would need.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenAsScopedRoleAsync(ServiceOrderDbContext db)
    {
        await using (var owner = await OpenAsync(db))
        {
            await using var grant = owner.CreateCommand();
            grant.CommandText = $"""
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ScopedRole}') THEN
                      CREATE ROLE {ScopedRole} LOGIN PASSWORD '{ScopedRole}';
                   END IF;
                END $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderTargets" TO {ScopedRole};
                GRANT SELECT ON "ServiceOrders" TO {ScopedRole};
                GRANT SELECT ON "OrganizationGrantMirror" TO {ScopedRole};
                """;
            await grant.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = ScopedRole,
            Password = ScopedRole,
            Pooling = false,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task SetOrganizationAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await using var setConfig = connection.CreateCommand();
        setConfig.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
        setConfig.Parameters.Add(new NpgsqlParameter("orgId", organizationId.ToString()));
        await setConfig.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> VisibleTargetIdsAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SetOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ServiceOrderTargets" ORDER BY "Id" """;
        var ids = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    private static async Task InsertOrderAsync(NpgsqlConnection connection, string id, Guid organizationId, string attributesJson = "{}")
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertOrderSql;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        insert.Parameters.Add(new NpgsqlParameter("attributesJson", attributesJson));
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task InsertTargetAsync(NpgsqlConnection connection, string id, string orderId, Guid organizationId, string featureId = "feat-1")
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertTargetSql;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        insert.Parameters.Add(new NpgsqlParameter("featureId", featureId));
        insert.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>A grant from <see cref="OwnerOrganization"/> to <see cref="GranteeOrganization"/>, as the sync worker would mirror it.</summary>
    private static async Task InsertMirroredGrantAsync(NpgsqlConnection connection, bool isActive, bool isExpired, string? resourceType, string? scopeId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO "OrganizationGrantMirror" ("GrantId", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "IsActive", "ExpiresAt", "SyncedAtUtc")
            VALUES (@id, @grantee, @resource, @resourceType, @scopeId, @isActive, @expiresAt, now())
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        insert.Parameters.Add(new NpgsqlParameter("grantee", GranteeOrganization));
        insert.Parameters.Add(new NpgsqlParameter("resource", OwnerOrganization));
        insert.Parameters.Add(new NpgsqlParameter("resourceType", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)resourceType ?? DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("scopeId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)scopeId ?? DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("isActive", isActive));
        insert.Parameters.Add(new NpgsqlParameter("expiresAt", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = isExpired ? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DBNull.Value,
        });
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReleaseTargetsAsync(NpgsqlConnection connection, string orderId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = """UPDATE "ServiceOrderTargets" SET "ReleasedAt" = now() WHERE "ServiceOrderId" = @orderId""";
        update.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        return await update.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteTargetAsync(NpgsqlConnection connection, string id)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText = """DELETE FROM "ServiceOrderTargets" WHERE "Id" = @id""";
        delete.Parameters.Add(new NpgsqlParameter("id", id));
        return await delete.ExecuteNonQueryAsync();
    }

    private static Task<List<string>> QueryAsync(ServiceOrderDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>(sql).ToListAsync();

    /// <summary>
    /// A context pointed at a database that does not exist yet — the migrator creates it,
    /// exactly as it would on a brand-new environment.
    /// </summary>
    private static async Task<ServiceOrderDbContext> NewContextOnEmptyDatabaseAsync()
    {
        var container = await EnsureContainerAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = $"fieldservice_chain_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssemblyName))
            .Options;
        return new ServiceOrderDbContext(options);
    }

    private static async Task<PostgreSqlContainer> EnsureContainerAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_container is not null) return _container;

            var container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase("geoassets_fieldservice_test")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await container.StartAsync();

            _container = container;
            return _container;
        }
        finally
        {
            StartLock.Release();
        }
    }
}
