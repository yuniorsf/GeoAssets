using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The XD01-315 pair <c>AddServiceOrderOccupancyChangeSets</c> /
/// <c>AddServiceOrderOccupancyChangeSetsRowLevelSecurity</c> in the
/// <c>GeoAssets.FieldService.Server</c> history: the table of staged occupancy changes as the
/// migration really creates it, up, down and up again, and its owner-only policy observed as a
/// non-owner role — including a grantee whose mirrored grant shows it the order's targets and
/// still none of the staged changes.
/// </summary>
public partial class FieldServiceMigrationChainTests
{
    private const string ChangeSetsTable = "ServiceOrderOccupancyChangeSets";
    private const string ChangeSetsTableMigration = "AddServiceOrderOccupancyChangeSets";
    private const string ChangeSetsRowLevelSecurityMigration = "AddServiceOrderOccupancyChangeSetsRowLevelSecurity";
    private const string ChangeSetsScopedRole = "fieldservice_change_set_scoped_role";

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
        (await QueryAsync(db, ChangeSetsRowLevelSecuritySql)).Should().Equal(["false:false:0"], "row-level security arrives with the next migration");
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
        chain.Skip(chain.IndexOf(rowLevelSecurity) + 1).Should().OnlyContain(
            later => string.CompareOrdinal(later, rowLevelSecurity) > 0,
            "whatever follows this pair was added by a later ticket (XD01-316's dispatch-recipient policies were the first)");

        // The schema every deployed database has today, with an order and its target stored.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, ChangeSetsTableExistsSql)).Should().Equal("false");
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-1", OwnerOrganization);
            await InsertTargetAsync(owner, "tgt-1", "ord-1", OwnerOrganization, "pole-1");
        }

        // Up: table, then policy.
        await migrator.MigrateAsync(table);
        await using (var owner = await OpenAsync(db))
            await InsertChangeSetAsync(owner, "ord-1", "tgt-1", "Staged", OwnerOrganization);
        await migrator.MigrateAsync(rowLevelSecurity);
        (await QueryAsync(db, ChangeSetsRowLevelSecuritySql)).Should().Equal(["true:false:1"], "enabled, not forced, exactly one policy");
        var policy = (await QueryAsync(db, ChangeSetsPolicySql)).Should().ContainSingle().Subject;
        const string OwnerOnly = """("OrganizationId" = (current_setting('app.current_organization_id'::text, true))::uuid)""";
        policy.Should().StartWith($"tenant_isolation_policy|PERMISSIVE|ALL|{OwnerOnly}|", "reading is the pole owner's alone");
        policy.Should().Contain("""FROM "ServiceOrderTargets" t""")
            .And.Contain("""(t."OrganizationId" = "ServiceOrderOccupancyChangeSets"."OrganizationId")""")
            .And.NotContain("OrganizationGrantMirror", "unlike the targets' policy in this history, no mirrored grant opens staged changes");

        // Down, one step: the policy goes, the table and its rows stay.
        await migrator.MigrateAsync(table);
        (await QueryAsync(db, ChangeSetsRowLevelSecuritySql)).Should().Equal("false:false:0");
        (await QueryAsync(db, ChangeSetStatusesSql)).Should().Equal("tgt-1:Staged");

        // Down, second step: the table goes; the order, its target and the occupancy tables stay.
        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, ChangeSetsTableExistsSql)).Should().Equal("false");
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain([table, rowLevelSecurity]);
        (await QueryAsync(db, """SELECT "Id"::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Equal("tgt-1");
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
        await using var owner = await OpenAsync(db);
        await InsertOrderAsync(owner, "ord-1", OwnerOrganization);
        await InsertTargetAsync(owner, "tgt-1", "ord-1", OwnerOrganization, "pole-1");
        await InsertTargetAsync(owner, "tgt-2", "ord-1", OwnerOrganization, "pole-2");
        await InsertChangeSetAsync(owner, "ord-1", "tgt-1", "Staged", OwnerOrganization);
        await InsertChangeSetAsync(owner, "ord-1", "tgt-1", "Discarded", OwnerOrganization);
        await InsertChangeSetAsync(owner, "ord-1", "tgt-1", "Discarded", OwnerOrganization);
        await InsertChangeSetAsync(owner, "ord-1", "tgt-2", "Staged", OwnerOrganization);
        var secondStaged = async () => await InsertChangeSetAsync(owner, "ord-1", "tgt-1", "Staged", OwnerOrganization);
        var withoutATarget = async () => await InsertChangeSetAsync(owner, "ord-1", "tgt-missing", "Staged", OwnerOrganization);

        (await secondStaged.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        (await withoutATarget.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

        (await DeleteTargetAsync(owner, "tgt-1")).Should().Be(1);
        (await QueryAsync(db, ChangeSetStatusesSql)).Should().Equal(["tgt-2:Staged"], "a target's sets are deleted with it, in every status");
        await using (var deleteOrder = owner.CreateCommand())
        {
            deleteOrder.CommandText = """DELETE FROM "ServiceOrders" WHERE "Id" = 'ord-1'""";
            await deleteOrder.ExecuteNonQueryAsync();
        }

        (await QueryAsync(db, ChangeSetStatusesSql)).Should().BeEmpty("and the rest with their order");
    }

    [Fact]
    public async Task OccupancyChangeSetsPolicy_AsANonOwnerRole_ShowsASetOnlyToThePoleOwner_NotEvenToAGranteeThatSeesTheTarget()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-owner", OwnerOrganization);
            await InsertTargetAsync(owner, "tgt-owner", "ord-owner", OwnerOrganization, "pole-1");
            await InsertOrderAsync(owner, "ord-stranger", StrangerOrganization);
            await InsertTargetAsync(owner, "tgt-stranger", "ord-stranger", StrangerOrganization, "pole-9");
            await InsertChangeSetAsync(owner, "ord-owner", "tgt-owner", "Staged", OwnerOrganization);
            await InsertChangeSetAsync(owner, "ord-owner", "tgt-owner", "Discarded", OwnerOrganization);
            await InsertChangeSetAsync(owner, "ord-stranger", "tgt-stranger", "Staged", StrangerOrganization);
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsChangeSetScopedRoleAsync(db);

        (await VisibleChangeSetsAsync(scoped, OwnerOrganization)).Should().Equal("tgt-owner:Discarded", "tgt-owner:Staged");
        (await VisibleChangeSetsAsync(scoped, StrangerOrganization)).Should().Equal("tgt-stranger:Staged");
        (await VisibleChangeSetsAsync(scoped, GranteeOrganization)).Should().BeEmpty("no mirrored grant widens the owner-only policy");
        (await VisibleTargetIdsAsync(scoped, GranteeOrganization)).Should().Equal(["tgt-owner"], "the grant is live: it shows the grantee the owner's target");
        (await VisibleChangeSetsAsync(scoped, OccupantOrganization)).Should().BeEmpty("an occupant named in a change does not see it");
    }

    [Fact]
    public async Task OccupancyChangeSetsPolicy_AsANonOwnerRole_RejectsWritesFromOutsideThePoleOwner_AndLetsTheOwnerWrite()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        await db.Database.MigrateAsync();
        await using (var owner = await OpenAsync(db))
        {
            await InsertOrderAsync(owner, "ord-owner", OwnerOrganization);
            await InsertTargetAsync(owner, "tgt-owner", "ord-owner", OwnerOrganization, "pole-1");
            await InsertTargetAsync(owner, "tgt-owner-2", "ord-owner", OwnerOrganization, "pole-2");
            await InsertOrderAsync(owner, "ord-owner-other", OwnerOrganization);
            await InsertChangeSetAsync(owner, "ord-owner", "tgt-owner", "Staged", OwnerOrganization);
            await InsertMirroredGrantAsync(owner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var scoped = await OpenAsChangeSetScopedRoleAsync(db);

        foreach (var outsider in new[] { GranteeOrganization, OccupantOrganization, StrangerOrganization })
        {
            await SetOrganizationAsync(scoped, outsider);
            var underItsOwnName = async () => await InsertChangeSetAsync(scoped, "ord-owner", "tgt-owner-2", "Staged", outsider);
            var underTheOwnersName = async () => await InsertChangeSetAsync(scoped, "ord-owner", "tgt-owner-2", "Staged", OwnerOrganization);

            (await underItsOwnName.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(
                "42501", "a row under another organization's target would take that target's one staged slot");
            (await underTheOwnersName.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
            await using var tamper = scoped.CreateCommand();
            tamper.CommandText =
                """
                UPDATE "ServiceOrderOccupancyChangeSets" SET "Status" = 'Discarded', "ClosedAt" = now();
                DELETE FROM "ServiceOrderOccupancyChangeSets";
                """;
            (await tamper.ExecuteNonQueryAsync()).Should().Be(0, "neither statement may reach a row of another organization");
        }

        // The owner: stages on its second target, cannot name another order than the target's, discards, deletes.
        await SetOrganizationAsync(scoped, OwnerOrganization);
        await InsertChangeSetAsync(scoped, "ord-owner", "tgt-owner-2", "Staged", OwnerOrganization);
        var wrongOrder = async () => await InsertChangeSetAsync(scoped, "ord-owner-other", "tgt-owner", "Discarded", OwnerOrganization);
        (await wrongOrder.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        await using (var discard = scoped.CreateCommand())
        {
            discard.CommandText = """UPDATE "ServiceOrderOccupancyChangeSets" SET "Status" = 'Discarded', "ClosedAt" = now() WHERE "TargetId" = 'tgt-owner-2'""";
            (await discard.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await using (var delete = scoped.CreateCommand())
        {
            delete.CommandText = """DELETE FROM "ServiceOrderOccupancyChangeSets" WHERE "TargetId" = 'tgt-owner'""";
            (await delete.ExecuteNonQueryAsync()).Should().Be(1);
        }

        (await VisibleChangeSetsAsync(scoped, OwnerOrganization)).Should().Equal("tgt-owner-2:Discarded");
    }

    /// <summary>
    /// A non-owner role holding what an application role needs for staged changes: privileges on
    /// the table, and SELECT on what its policy's check reads — the target, and through the
    /// targets' own policy in this history the order and the mirrored grants.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenAsChangeSetScopedRoleAsync(ServiceOrderDbContext db)
    {
        await using (var owner = await OpenAsync(db))
        {
            await using var grant = owner.CreateCommand();
            grant.CommandText = $"""
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ChangeSetsScopedRole}') THEN
                      CREATE ROLE {ChangeSetsScopedRole} LOGIN PASSWORD '{ChangeSetsScopedRole}';
                   END IF;
                END $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderOccupancyChangeSets" TO {ChangeSetsScopedRole};
                GRANT SELECT ON "ServiceOrderTargets" TO {ChangeSetsScopedRole};
                GRANT SELECT ON "ServiceOrders" TO {ChangeSetsScopedRole};
                GRANT SELECT ON "OrganizationGrantMirror" TO {ChangeSetsScopedRole};
                """;
            await grant.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = ChangeSetsScopedRole,
            Password = ChangeSetsScopedRole,
            Pooling = false,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>One change set whose single change names <see cref="OccupantOrganization"/> as the occupant.</summary>
    private static async Task InsertChangeSetAsync(NpgsqlConnection connection, string orderId, string targetId, string status, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO "ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
            VALUES (gen_random_uuid(), @order, @target, 'pole-1', @changes, 0, @status, 'tester', now(), CASE WHEN @status = 'Staged' THEN NULL ELSE now() END, @org)
            """;
        insert.Parameters.Add(new NpgsqlParameter("order", orderId));
        insert.Parameters.Add(new NpgsqlParameter("target", targetId));
        insert.Parameters.Add(new NpgsqlParameter("changes", $$"""[{"kind":"MarkUnidentified","slotIndex":1,"occupantOrganizationId":"{{OccupantOrganization}}"}]"""));
        insert.Parameters.Add(new NpgsqlParameter("status", status));
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>Every change set the session sees as <paramref name="organizationId"/>, as <c>target:status</c>.</summary>
    private static async Task<List<string>> VisibleChangeSetsAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SetOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "TargetId" || ':' || "Status" FROM "ServiceOrderOccupancyChangeSets" ORDER BY 1""";
        var lines = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return lines;
    }
}
