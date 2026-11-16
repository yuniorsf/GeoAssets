using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// Tenant isolation of the whole isolated FieldService database (XD01-323), observed with raw
/// SQL as a non-owner role on the <b>real</b> migration chain — never through an entity, so
/// these prove the policies themselves and not a query filter. The twin of
/// <c>CrossTenantRowLevelSecurityTests</c> in GeoAssets.Security.Tests, which does the same for
/// the shared-database history carried by <c>GeoAssets.Server</c>; grants are read here from
/// <c>"OrganizationGrantMirror"</c> instead of <c>"OrganizationGrants"</c>.
///
/// Three organizations: <c>OwnerOrganization</c> ("tenant A"), <c>StrangerOrganization</c>
/// ("tenant B", unrelated to A) and <c>GranteeOrganization</c> (holds whatever mirrored grant a
/// fact gives it on A's resources).
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string TenantScopedRole = "fieldservice_tenant_scoped_role";
    private const string RowSecurityViolation = "42501";

    /// <summary>
    /// The tables of this database that hold no organization's data, each with the reason. A
    /// table is exempt from the "has a policy" fact only by being named here, and the fact
    /// itself checks that nothing named here carries an organization column or hangs off a
    /// protected table.
    /// </summary>
    private static readonly Dictionary<string, string> TablesWithoutTenantData = new(StringComparer.Ordinal)
    {
        ["__EFMigrationsHistory"] = "EF Core's own bookkeeping of applied migrations",
        ["OrderTypes"] = "the order type catalogue, shared by every organization",
        ["OrderTypeStates"] = "states of a shared order type",
        ["OrderTypeTransitions"] = "transitions of a shared order type",
        ["OrderActionPermissions"] = "role permissions of a shared order type",
        ["OrderCreationPolicies"] = "creation policies of a shared order type",
        ["WorkflowOutboxMessages"] = "messaging outbox, drained by a background relay that runs for no organization",
        ["WorkflowInboxProcessedMessages"] = "message deduplication keys, written by background consumers",
        ["OrganizationGrantSyncCursor"] = "the grant sync worker's one-row position",
        [GrantMirrorTable] = "read by the policies themselves on behalf of every organization, written by the grant sync worker",
    };

    private const string GrantMirrorTable = "OrganizationGrantMirror";

    /// <summary>The tables with an equality-only policy, and what a fact needs to put a row in each.</summary>
    private static readonly Dictionary<string, TenantTable> EqualityTables = new(StringComparer.Ordinal)
    {
        ["ContractorUsLedgerEntries"] = new(
            "\"ContractorUsLedgerEntries\"", "OrganizationId",
            """
            INSERT INTO "ContractorUsLedgerEntries" ("Id", "OrganizationId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
            VALUES (@id, @org, 'US-1', '', 1, 1, '[]', now())
            """),
        ["UsCatalogItems"] = new(
            "\"UsCatalogItems\"", "OrganizationId",
            """
            INSERT INTO "UsCatalogItems" ("Id", "OrganizationId", "ItemCode", "Description", "Unit", "BaseUsFactor", "RegionalMultiplier", "MonetaryConversionRate", "EffectiveFrom")
            VALUES (@id, @org, 'ITEM-1', '', 'un', 1, 1, 1, now())
            """),
        ["field_manhole_audits"] = new(
            "field_manhole_audits", "OrganizationId",
            """
            INSERT INTO field_manhole_audits ("Id", "ConduitFeatureId", "OrganizationId", "BoreOccupancyPercentage", "SubDuctsJson", "SpliceEnclosureCondition", "Nr33ChecklistAttributesJson", "AuditedBy", "AuditedAtUtc")
            VALUES (@id, 'conduit-1', @org, 10, '[]', 'ok', '{}', 'tester', now())
            """),
        ["field_icerp_paving_measurements"] = new(
            "field_icerp_paving_measurements", "OrganizationId",
            """
            INSERT INTO field_icerp_paving_measurements ("Id", "ParentServiceOrderId", "ChildServiceOrderId", "OrganizationId", "LengthMeters", "WidthMeters", "DepthMeters", "RecordedAreaSquareMeters", "ActualAreaSquareMeters", "VolumeCubicMeters", "RecordedAtUtc")
            VALUES (@id, @orderId, @orderId, @org, 1, 1, 1, 1, 1, 1, now())
            """),
        ["field_contractor_us_postings"] = new(
            "field_contractor_us_postings", "ContractorOrganizationId",
            """
            INSERT INTO field_contractor_us_postings ("Id", "ServiceOrderId", "ContractorOrganizationId", "ItemCode", "Quantity", "UnitUs", "TotalUs", "MonetaryRate", "TotalBrl", "PostedAtUtc")
            VALUES (@id, @orderId, @org, 'ITEM-1', 1, 1, 1, 1, 1, now())
            """),
    };

    /// <summary>The two tables whose rows follow their order.</summary>
    private static readonly Dictionary<string, string> OrderChildInserts = new(StringComparer.Ordinal)
    {
        ["OrderActionLogs"] =
            """
            INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "PerformedBy", "PerformedAt", "ActorKind")
            VALUES (@orderId, 0, 'tester', now(), 0)
            """,
        ["OrderDispatches"] =
            """
            INSERT INTO "OrderDispatches" ("ServiceOrderId", "TargetId", "TargetType", "DispatchedBy", "DispatchedAt", "ActorKind")
            VALUES (@orderId, 'crew-1', 0, 'tester', now(), 0)
            """,
    };

    private const string InsertOrderWithoutRowVersionSql =
        """
        INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
        VALUES (@id, @id, '', 'inspection', 'Draft', 0, 'tester', @org, now(), @attributesJson, '[]')
        """;

    private sealed record TenantTable(string QuotedName, string OrganizationColumn, string InsertSql);

    // ── Every table is either protected or named as holding no tenant data ────

    /// <summary>
    /// The FieldService twin of the shared-database suite's "every organization-scoped table has
    /// row-level security" fact, and stricter: it starts from the tables the migrated database
    /// really has, not from the model's <c>OrganizationId</c> properties, so a table that is
    /// tenant-scoped through a differently named column (<c>field_contractor_us_postings</c>) or
    /// only through its parent (<c>"OrderActionLogs"</c>) is not overlooked. A table added by a
    /// later migration fails here until it gets a policy or a line, with its reason, in
    /// <see cref="TablesWithoutTenantData"/>.
    /// </summary>
    [Fact]
    public async Task EveryTable_HasRowLevelSecurityWithAPolicy_UnlessItIsNamedAsHoldingNoTenantData()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();

        var tables = (await QueryAsync(db,
            """
            SELECT (c.relname || '|' || c.relrowsecurity::text || '|'
                    || (SELECT count(*) FROM pg_policies p WHERE p.schemaname = 'public' AND p.tablename = c.relname)::text) AS "Value"
            FROM pg_class c
            WHERE c.relkind IN ('r', 'p') AND c.relnamespace = 'public'::regnamespace
            """)).Select(line => line.Split('|')).ToDictionary(parts => parts[0], parts => (Enabled: string.Equals(parts[1], "true", StringComparison.Ordinal), Policies: int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)), StringComparer.Ordinal);
        var organizationColumns = (await QueryAsync(db,
            """
            SELECT (table_name || '|' || column_name)::text AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name ILIKE '%organization%'
            """)).Select(line => line.Split('|')[0]).ToHashSet(StringComparer.Ordinal);
        var parentsByTable = (await QueryAsync(db,
            """
            SELECT (conrelid::regclass::text || '|' || confrelid::regclass::text) AS "Value" FROM pg_constraint
            WHERE contype = 'f' AND connamespace = 'public'::regnamespace
            """)).Select(line => line.Replace("\"", string.Empty, StringComparison.Ordinal).Split('|')).ToLookup(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        tables.Should().ContainKeys("ServiceOrders", "ContractorUsLedgerEntries", "OrderActionLogs", "field_contractor_us_postings");
        tables.Keys.Should().Contain(TablesWithoutTenantData.Keys, "an entry for a table that no longer exists must be removed, not left to excuse a future one");

        foreach (var (table, state) in tables)
        {
            if (TablesWithoutTenantData.TryGetValue(table, out var reason))
            {
                if (!string.Equals(table, GrantMirrorTable, StringComparison.Ordinal))
                    organizationColumns.Should().NotContain(table, $"'{table}' is exempt as \"{reason}\", which a column naming an organization contradicts");
                parentsByTable[table].Where(parent => !TablesWithoutTenantData.ContainsKey(parent)).Should().BeEmpty(
                    $"'{table}' is exempt as \"{reason}\", but a table reachable only through a tenant-scoped one is tenant-scoped itself");
                continue;
            }

            state.Enabled.Should().BeTrue($"table '{table}' is not named as holding no tenant data, so it must have row-level security enabled");
            state.Policies.Should().BeGreaterThan(0, $"table '{table}' with row-level security but no policy denies every row, which means its policy was dropped or never created");
        }
    }

    // ── Equality-only tables: one fact, run for each ──────────────────────────

    [Theory]
    [InlineData("ContractorUsLedgerEntries")]
    [InlineData("UsCatalogItems")]
    [InlineData("field_manhole_audits")]
    [InlineData("field_icerp_paving_measurements")]
    [InlineData("field_contractor_us_postings")]
    public async Task EqualityTable_AsANonOwnerRole_HidesAndProtectsAnotherTenantsRows_EvenFromAGrantee(string tableName)
    {
        var table = EqualityTables[tableName];
        var rowOfA = Guid.NewGuid();
        var rowOfB = Guid.NewGuid();
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-a", OwnerOrganization);
            await InsertOrderAsync(owner, "ord-b", StrangerOrganization);
            await InsertTenantRowAsync(owner, table, rowOfA, OwnerOrganization, "ord-a");
            await InsertTenantRowAsync(owner, table, rowOfB, StrangerOrganization, "ord-b");
            // The widest grant there is, from A to the grantee: it opens A's orders, and nothing here.
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);

        // Read.
        (await VisibleRowIdsAsync(scoped, table)).Should().BeEmpty("a session that never set an organization belongs to none");
        await SetOrganizationAsync(scoped, GranteeOrganization);
        (await VisibleRowIdsAsync(scoped, table)).Should().BeEmpty("no mirrored grant widens an equality-only policy");
        await SetOrganizationAsync(scoped, OwnerOrganization);
        (await VisibleRowIdsAsync(scoped, table)).Should().Equal(rowOfA);
        await SetOrganizationAsync(scoped, StrangerOrganization);
        (await VisibleRowIdsAsync(scoped, table)).Should().Equal(rowOfB);

        // Write, as tenant B.
        var forged = async () => await InsertTenantRowAsync(scoped, table, Guid.NewGuid(), OwnerOrganization, "ord-a");
        var handedOver = async () => await ExecuteAsync(scoped,
            $"""UPDATE {table.QuotedName} SET "{table.OrganizationColumn}" = @org WHERE "Id" = @id""", ("org", OwnerOrganization), ("id", rowOfB));
        (await forged.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await handedOver.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await ExecuteAsync(scoped, $"""UPDATE {table.QuotedName} SET "Id" = "Id" WHERE "Id" = @id""", ("id", rowOfA))).Should().Be(0);
        (await ExecuteAsync(scoped, $"""DELETE FROM {table.QuotedName} WHERE "Id" = @id""", ("id", rowOfA))).Should().Be(0);
        (await ExecuteAsync(scoped, $"""UPDATE {table.QuotedName} SET "Id" = "Id" """)).Should().Be(1, "an unqualified update reaches the tenant's own row only");
        var ownNewRow = Guid.NewGuid();
        await InsertTenantRowAsync(scoped, table, ownNewRow, StrangerOrganization, "ord-b");
        (await ExecuteAsync(scoped, $"""DELETE FROM {table.QuotedName} WHERE "Id" = @id""", ("id", ownNewRow))).Should().Be(1);

        // Tenant A's row is exactly as it was.
        (await QueryAsync(db, $"""SELECT "Id"::text AS "Value" FROM {table.QuotedName} ORDER BY "{table.OrganizationColumn}" """))
            .Should().Equal(rowOfA.ToString(), rowOfB.ToString());
    }

    // ── "ServiceOrders": own organization, or a mirrored grant ────────────────

    [Fact]
    public async Task ServiceOrders_AsANonOwnerRole_AreHiddenFromAndProtectedAgainstAnotherTenant()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-a", OwnerOrganization);
            await InsertOrderAsync(owner, "ord-b", StrangerOrganization);
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);

        // Read.
        (await VisibleOrderIdsAsync(scoped)).Should().BeEmpty("a session that never set an organization belongs to none");
        await SetOrganizationAsync(scoped, OwnerOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal("ord-a");
        await SetOrganizationAsync(scoped, GranteeOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().BeEmpty("it has no grant");
        await SetOrganizationAsync(scoped, StrangerOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal("ord-b");

        // Write, as tenant B.
        var forged = async () => await InsertOrderWithoutRowVersionAsync(scoped, "ord-forged", OwnerOrganization);
        var handedOver = async () => await ExecuteAsync(scoped,
            """UPDATE "ServiceOrders" SET "OrganizationId" = @org WHERE "Id" = 'ord-b'""", ("org", OwnerOrganization));
        (await forged.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await handedOver.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Title" = 'tampered' WHERE "Id" = 'ord-a'""")).Should().Be(0);
        (await ExecuteAsync(scoped, """DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-a'""")).Should().Be(0);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Title" = 'renamed by its owner'""")).Should().Be(1, "an unqualified update reaches the tenant's own order only");
        await InsertOrderWithoutRowVersionAsync(scoped, "ord-b-2", StrangerOrganization);
        (await ExecuteAsync(scoped, """DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-b-2'""")).Should().Be(1);

        (await QueryAsync(db, """SELECT ("Id" || ':' || "Title")::text AS "Value" FROM "ServiceOrders" ORDER BY "Id" """))
            .Should().Equal("ord-a:ord-a", "ord-b:renamed by its owner");
    }

    /// <summary>
    /// The grant conditions of the shared database's policy, one case each (the cases of
    /// <c>ServiceOrdersRowLevelSecurityPolicyTests</c> in GeoAssets.Workflow.EFCore.Tests), read
    /// here from the mirror: grantee and resource organization, active, unexpired, resource type
    /// absent or "ServiceOrder", scope id absent or equal to the order's own.
    /// </summary>
    [Theory]
    [InlineData(true, false, null, null, null, true)]
    [InlineData(false, false, null, null, null, false)]
    [InlineData(true, true, null, null, null, false)]
    [InlineData(true, false, "AssetLayer", null, null, false)]
    [InlineData(true, false, "ServiceOrder", null, null, true)]
    [InlineData(true, false, null, "district-7", "district-7", true)]
    [InlineData(true, false, null, "district-9", "district-7", false)]
    [InlineData(true, false, null, "district-7", null, false)]
    [InlineData(true, false, null, null, "district-7", true)]
    public async Task ServiceOrders_AsANonOwnerRole_AreOpenedToAGrantee_ExactlyWhenTheMirroredGrantCoversTheOrder(
        bool grantIsActive, bool grantIsExpired, string? grantResourceType, string? grantScopeId, string? orderScopeId, bool expectGranteeReachesTheOrder)
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-a", OwnerOrganization, orderScopeId is null ? "{}" : $$"""{"scopeId":"{{orderScopeId}}"}""");
            await InsertOrderAsync(owner, "ord-b", StrangerOrganization);
            await InsertMirroredGrantAsync(owner, grantIsActive, grantIsExpired, grantResourceType, grantScopeId);
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);

        await SetOrganizationAsync(scoped, StrangerOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal(["ord-b"], "the grant names another grantee");
        await SetOrganizationAsync(scoped, GranteeOrganization);
        (await VisibleOrderIdsAsync(scoped)).Should().Equal(expectGranteeReachesTheOrder ? ["ord-a"] : []);
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Title" = 'edited by the grantee'"""))
            .Should().Be(expectGranteeReachesTheOrder ? 1 : 0, "a grantee edits a granted order, as the shared database's policy lets it, and no other");
    }

    [Fact]
    public async Task ServiceOrders_AsANonOwnerRole_KeepAScopedGranteeInsideItsScopeWhenItWrites()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-in-scope", OwnerOrganization, """{"scopeId":"district-7"}""");
            await InsertOrderAsync(owner, "ord-out-of-scope", OwnerOrganization, """{"scopeId":"district-9"}""");
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: "ServiceOrder", scopeId: "district-7");
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        await SetOrganizationAsync(scoped, GranteeOrganization);

        (await VisibleOrderIdsAsync(scoped)).Should().Equal("ord-in-scope");
        (await ExecuteAsync(scoped, """UPDATE "ServiceOrders" SET "Title" = 'tampered' WHERE "Id" = 'ord-out-of-scope'""")).Should().Be(0);
        (await ExecuteAsync(scoped, """DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-out-of-scope'""")).Should().Be(0);
        var movedOutOfScope = async () => await ExecuteAsync(scoped,
            """UPDATE "ServiceOrders" SET "AttributesJson" = @attributesJson WHERE "Id" = 'ord-in-scope'""", ("attributesJson", """{"scopeId":"district-9"}"""));
        var createdOutOfScope = async () => await InsertOrderWithoutRowVersionAsync(scoped, "ord-new-out", OwnerOrganization, """{"scopeId":"district-9"}""");
        var createdForAThirdParty = async () => await InsertOrderWithoutRowVersionAsync(scoped, "ord-new-third", StrangerOrganization, """{"scopeId":"district-7"}""");
        (await movedOutOfScope.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await createdOutOfScope.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await createdForAThirdParty.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);

        // Inside its scope it writes under the resource organization, as in the shared database.
        await InsertOrderWithoutRowVersionAsync(scoped, "ord-new-in", OwnerOrganization, """{"scopeId":"district-7"}""");
        (await VisibleOrderIdsAsync(scoped)).Should().Equal("ord-in-scope", "ord-new-in");
    }

    // ── "OrderActionLogs" and "OrderDispatches": follow their order ───────────

    [Theory]
    [InlineData("OrderActionLogs", true, null, true)]
    [InlineData("OrderActionLogs", false, null, false)]
    [InlineData("OrderActionLogs", true, "district-7", true)]
    [InlineData("OrderActionLogs", true, "district-9", false)]
    [InlineData("OrderDispatches", true, null, true)]
    [InlineData("OrderDispatches", false, null, false)]
    [InlineData("OrderDispatches", true, "district-7", true)]
    [InlineData("OrderDispatches", true, "district-9", false)]
    public async Task OrderChildTable_AsANonOwnerRole_FollowsItsOrder_ForReadsAndWrites(
        string tableName, bool grantIsActive, string? grantScopeId, bool expectGranteeReachesTheOrder)
    {
        var insertSql = OrderChildInserts[tableName];
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-a", OwnerOrganization, """{"scopeId":"district-7"}""");
            await InsertOrderAsync(owner, "ord-b", StrangerOrganization);
            await ExecuteAsync(owner, insertSql, ("orderId", "ord-a"));
            await ExecuteAsync(owner, insertSql, ("orderId", "ord-b"));
            await InsertMirroredGrantAsync(owner, grantIsActive, isExpired: false, resourceType: null, grantScopeId);
        }

        await using var scoped = await OpenAsTenantScopedRoleAsync(db);
        var visibleSql = $"""SELECT "ServiceOrderId"::text AS "Value" FROM "{tableName}" ORDER BY "Id" """;

        // Read.
        (await ReadStringsAsync(scoped, visibleSql)).Should().BeEmpty("a session that never set an organization belongs to none");
        await SetOrganizationAsync(scoped, OwnerOrganization);
        (await ReadStringsAsync(scoped, visibleSql)).Should().Equal("ord-a");
        await SetOrganizationAsync(scoped, GranteeOrganization);
        (await ReadStringsAsync(scoped, visibleSql)).Should().Equal(expectGranteeReachesTheOrder ? ["ord-a"] : []);
        await SetOrganizationAsync(scoped, StrangerOrganization);
        (await ReadStringsAsync(scoped, visibleSql)).Should().Equal("ord-b");

        // Write, as tenant B: nothing under or into tenant A's order.
        var forged = async () => await ExecuteAsync(scoped, insertSql, ("orderId", "ord-a"));
        var movedUnderAnothersOrder = async () => await ExecuteAsync(scoped, $"""UPDATE "{tableName}" SET "ServiceOrderId" = 'ord-a'""");
        (await forged.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await movedUnderAnothersOrder.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);
        (await ExecuteAsync(scoped, $"""DELETE FROM "{tableName}" WHERE "ServiceOrderId" = 'ord-a'""")).Should().Be(0);
        (await ExecuteAsync(scoped, insertSql, ("orderId", "ord-b"))).Should().Be(1);

        // Write, as the grantee: it appends to the order exactly when it can see it.
        await SetOrganizationAsync(scoped, GranteeOrganization);
        var appended = async () => await ExecuteAsync(scoped, insertSql, ("orderId", "ord-a"));
        if (expectGranteeReachesTheOrder)
            (await appended()).Should().Be(1);
        else
            (await appended.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(RowSecurityViolation);

        (await QueryAsync(db, $"""SELECT ("ServiceOrderId" || ':' || count(*))::text AS "Value" FROM "{tableName}" GROUP BY "ServiceOrderId" ORDER BY 1"""))
            .Should().Equal(expectGranteeReachesTheOrder ? "ord-a:2" : "ord-a:1", "ord-b:2");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A non-owner role with full privileges on every tenant-scoped table and SELECT on the grant
    /// mirror — what an application role would hold. Row-level security does not restrict a
    /// table's owner (no FORCE, as in the shared database), so the policies are only observable
    /// through such a role.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenAsTenantScopedRoleAsync(ServiceOrderDbContext db)
    {
        await using (var owner = await OpenAsync(db))
        {
            await ExecuteAsync(owner, $"""
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{TenantScopedRole}') THEN
                      CREATE ROLE {TenantScopedRole} LOGIN PASSWORD '{TenantScopedRole}';
                   END IF;
                END $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrders", "OrderActionLogs", "OrderDispatches", "ServiceOrderTargets",
                    "ContractorUsLedgerEntries", "UsCatalogItems", field_manhole_audits, field_icerp_paving_measurements,
                    field_contractor_us_postings TO {TenantScopedRole};
                GRANT SELECT ON "{GrantMirrorTable}" TO {TenantScopedRole};
                """);
        }

        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = TenantScopedRole,
            Password = TenantScopedRole,
            Pooling = false,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static Task<int> InsertTenantRowAsync(NpgsqlConnection connection, TenantTable table, Guid id, Guid organizationId, string orderId) =>
        ExecuteAsync(connection, table.InsertSql, ("id", id), ("org", organizationId), ("orderId", orderId));

    private static Task<int> InsertOrderWithoutRowVersionAsync(NpgsqlConnection connection, string id, Guid organizationId, string attributesJson = "{}") =>
        ExecuteAsync(connection, InsertOrderWithoutRowVersionSql, ("id", id), ("org", organizationId), ("attributesJson", attributesJson));

    private static Task<List<string>> VisibleOrderIdsAsync(NpgsqlConnection connection) =>
        ReadStringsAsync(connection, """SELECT "Id"::text AS "Value" FROM "ServiceOrders" ORDER BY "Id" """);

    private static async Task<List<Guid>> VisibleRowIdsAsync(NpgsqlConnection connection, TenantTable table) =>
        (await ReadStringsAsync(connection, $"""SELECT "Id"::text AS "Value" FROM {table.QuotedName}""")).ConvertAll(Guid.Parse);

    /// <summary>
    /// Runs <paramref name="sql"/> on <paramref name="connection"/> itself — same session, same
    /// role, same <c>app.current_organization_id</c> — with the parameters it actually names, and
    /// returns the affected row count. Raw SQL: no entity, so no query filter, takes part.
    /// </summary>
    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var session = OnConnection(connection);
        return await session.Database.ExecuteSqlRawAsync(
            EscapeBraces(sql),
            parameters
                .Where(parameter => sql.Contains("@" + parameter.Name, StringComparison.Ordinal))
                .Select(parameter => new NpgsqlParameter(parameter.Name, parameter.Value)));
    }

    /// <summary>The single text column, named "Value", that <paramref name="sql"/> returns on <paramref name="connection"/> itself.</summary>
    private static async Task<List<string>> ReadStringsAsync(NpgsqlConnection connection, string sql)
    {
        await using var session = OnConnection(connection);
        return await session.Database.SqlQueryRaw<string>(EscapeBraces(sql)).ToListAsync();
    }

    /// <summary>EF reads raw SQL as a composite format string; the SQL here is literal, JSON braces included.</summary>
    private static string EscapeBraces(string sql) =>
        sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

    /// <summary>A context that borrows an open connection: it neither closes nor disposes it.</summary>
    private static ServiceOrderDbContext OnConnection(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(connection).Options);
}
