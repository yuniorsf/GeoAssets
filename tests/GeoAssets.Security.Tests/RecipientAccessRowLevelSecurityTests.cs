using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-333 in the database, <c>GeoAssets.Server</c> history: what the organization an order was
/// dispatched to may write depends on the order type's <c>"RecipientAccess"</c> — 0 Inform: no
/// order, no target; 1 Execute: the order's targets, not the order; 2 Edit: both. Run against the
/// real migrated schema, in a database of this class's own (see <see cref="EnsureDatabaseAsync"/>),
/// as the non-owner <see cref="SecurityTestContainerFixture.TenantScopedRole"/>.
///
/// The database knows organizations, not users or roles, so the role half of the decision is
/// the application's (<c>ServiceOrderRecipientAccessTests</c>,
/// <c>ServiceOrderRecipientAccessEndpointTests</c>); here every level is crossed with the owner,
/// the recipient, a grantee and a stranger.
/// </summary>
public sealed class RecipientAccessRowLevelSecurityTests
{
    private static readonly Guid Owner = Guid.Parse("e1e1e1e1-0000-0000-0000-000000000333");
    private static readonly Guid Occupant = Guid.Parse("e2e2e2e2-0000-0000-0000-000000000333");
    private static readonly Guid Grantee = Guid.Parse("e3e3e3e3-0000-0000-0000-000000000333");
    private static readonly Guid Stranger = Guid.Parse("e4e4e4e4-0000-0000-0000-000000000333");

    private const string Inform = "ra-inform";
    private const string Execute = "ra-execute";
    private const string Edit = "ra-edit";
    private const string EditByOtherCase = "ra-edit-other-case";
    private const string OutOfRange = "ra-out-of-range";
    private const string NoTypeRow = "ra-no-type-row";
    private const string SecondExecute = "ra-execute-2";
    private const string EditNotDispatched = "ra-edit-not-dispatched";

    /// <summary>Every order of this class, each with one target of its own (<c>tgt-{order}</c>).</summary>
    private static readonly string[] AllOrders = [Inform, Execute, Edit, EditByOtherCase, OutOfRange, NoTypeRow, SecondExecute, EditNotDispatched];

    // ── The recipient, level by level ─────────────────────────────────────────

    [Fact]
    public async Task Orders_ARecipient_UpdatesAnOrderDispatchedToIt_OnlyWhenItsTypeLetsItEdit()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        var updated = await TouchEachOrderAsync(connection);

        updated.Should().Equal(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Inform] = 0,
            [Execute] = 0,
            [Edit] = 1,
            [EditByOtherCase] = 1,
            [OutOfRange] = 0,
            [NoTypeRow] = 0,
            [SecondExecute] = 0,
            [EditNotDispatched] = 0,
        }, "Inform and Execute leave the order to its owner; a type matches whatever its letter case; a level outside 0..2 or a type with no row counts as Inform; a type grants nothing on an order not dispatched to the recipient");
    }

    [Fact]
    public async Task Targets_ARecipient_UpdatesTheTargetsOfAnOrderDispatchedToIt_OnlyWhenItsTypeLetsItExecuteOrEdit()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        var updated = await CompleteEachOrdersTargetsAsync(connection);

        updated.Should().Equal(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Inform] = 0,
            [Execute] = 1,
            [Edit] = 1,
            [EditByOtherCase] = 1,
            [OutOfRange] = 0,
            [NoTypeRow] = 0,
            [SecondExecute] = 1,
            [EditNotDispatched] = 0,
        });
        (await TargetStatusAsync(Execute)).Should().Be("Done", "the update really was written");
        (await TargetStatusAsync(Inform)).Should().Be("Pending");
    }

    [Theory]
    [InlineData(Inform)]
    [InlineData(Execute)]
    [InlineData(Edit)]
    public async Task Targets_ARecipient_NeitherAddsNorRemovesATarget_AtAnyLevel(string orderId)
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        var addUnderOwner = async () => await InsertTargetAsync(connection, $"tgt-forged-{orderId}", orderId, "pole-forged", Owner);
        var addUnderItself = async () => await InsertTargetAsync(connection, $"tgt-forged-{orderId}", orderId, "pole-forged", Occupant);
        var deleted = await ExecuteAsync(connection, """DELETE FROM "ServiceOrderTargets" WHERE "ServiceOrderId" = @order""", ("order", orderId));

        (await addUnderOwner.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await addUnderItself.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        deleted.Should().Be(0);
        (await TargetCountAsync(orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Targets_ARecipientOfAnExecuteOrder_CannotMoveATargetToAnotherOrder_NorRelabelIt()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        var moveToAnotherExecuteOrder = async () => await ExecuteAsync(
            connection, """UPDATE "ServiceOrderTargets" SET "ServiceOrderId" = @to WHERE "Id" = @target""",
            ("to", SecondExecute), ("target", $"tgt-{Execute}"));
        var relabel = async () => await ExecuteAsync(
            connection, """UPDATE "ServiceOrderTargets" SET "OrganizationId" = @org WHERE "Id" = @target""",
            ("org", Occupant), ("target", $"tgt-{Execute}"));

        (await moveToAnotherExecuteOrder.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be("42501", "even between two orders it may execute, a target stays on its order");
        (await relabel.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "a target keeps its order's organization");
        (await TargetCountAsync(Execute)).Should().Be(1);
    }

    [Fact]
    public async Task Orders_ARecipientOfAnEditOrder_StillCannotRelabelIt_NorInsertOrDeleteOne()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        var relabel = async () => await ExecuteAsync(
            connection, """UPDATE "ServiceOrders" SET "OrganizationId" = @org WHERE "Id" = @order""", ("org", Occupant), ("order", Edit));
        var insert = async () => await InsertOrderAsync(connection, "ra-forged", Owner, "rls-edit");
        var deleted = await ExecuteAsync(connection, """DELETE FROM "ServiceOrders" WHERE "Id" = @order""", ("order", Edit));

        (await relabel.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task Orders_ARecipientStillReadsEveryOrderDispatchedToIt_AndItsTargets_WhateverTheLevel()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        (await ReadIdsAsync(connection, """SELECT "Id"::text AS "Value" FROM "ServiceOrders" WHERE "Id" LIKE 'ra-%' ORDER BY "Id" COLLATE "C" """))
            .Should().Equal(AllOrders.Where(o => !string.Equals(o, EditNotDispatched, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        (await ReadIdsAsync(connection, """SELECT "ServiceOrderId"::text AS "Value" FROM "ServiceOrderTargets" WHERE "ServiceOrderId" LIKE 'ra-%' ORDER BY "ServiceOrderId" COLLATE "C" """))
            .Should().Equal(AllOrders.Where(o => !string.Equals(o, EditNotDispatched, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    // ── Nobody else changed ──────────────────────────────────────────────────

    [Fact]
    public async Task OwnerAndGrantee_WriteEveryOrderAndTarget_WhateverTheLevel()
    {
        await SeedAsync();
        await GrantAsync();
        await using var connection = await OpenScopedRoleAsync();

        foreach (var (organization, name) in new[] { (Owner, "owner"), (Grantee, "grantee") })
        {
            await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organization);
            (await TouchEachOrderAsync(connection)).Values.Should().AllBeEquivalentTo(1, "the {0}", name);
            (await CompleteEachOrdersTargetsAsync(connection)).Values.Should().AllBeEquivalentTo(1, "the {0}", name);
            await InsertTargetAsync(connection, $"tgt-new-{name}", Edit, $"pole-{name}", Owner);
            (await ExecuteAsync(connection, """DELETE FROM "ServiceOrderTargets" WHERE "Id" = @target""", ("target", $"tgt-new-{name}"))).Should().Be(1);
        }
    }

    [Fact]
    public async Task AStranger_WritesNothing_WhateverTheLevel()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Stranger);

        (await TouchEachOrderAsync(connection)).Values.Should().AllBeEquivalentTo(0);
        (await CompleteEachOrdersTargetsAsync(connection)).Values.Should().AllBeEquivalentTo(0);
    }

    [Fact]
    public async Task TheFunctions_AnswerOnlyAboutTheSessionsOwnOrganization()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);
        (await AccessAsync(connection, Inform)).Should().Be("true:false:false", "dispatched to it, level 0");
        (await AccessAsync(connection, Execute)).Should().Be("true:true:false");
        (await AccessAsync(connection, Edit)).Should().Be("true:true:true");
        (await AccessAsync(connection, EditNotDispatched)).Should().Be("false:false:false");
        (await AccessAsync(connection, "ra-no-such-order")).Should().Be("false:false:false");

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        (await AccessAsync(connection, Edit)).Should().Be("false:false:false", "the owner is not a recipient; its own policy lets it write");

        (await StaysAsync(connection, $"tgt-{Execute}", Execute)).Should().BeTrue();
        (await StaysAsync(connection, $"tgt-{Execute}", SecondExecute)).Should().BeFalse();
        (await StaysAsync(connection, "tgt-no-such-target", Execute)).Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly SemaphoreSlim DatabaseLock = new(1, 1);
    private static string? _databaseConnectionString;

    /// <summary>
    /// A database of this class's own in the suite's container, migrated with the real chains, so
    /// no other class empties its tables mid-fact; the suite's non-owner role is granted the tables
    /// these tests touch.
    /// </summary>
    private static async Task<string> EnsureDatabaseAsync()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            if (_databaseConnectionString is not null) return _databaseConnectionString;

            var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
            {
                Database = "recipient_access_rls",
            }.ConnectionString;

            await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
                await identity.Database.MigrateAsync();
            await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
            {
                await serviceOrders.Database.MigrateAsync();
                await serviceOrders.Database.ExecuteSqlRawAsync($"""
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrders" TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderTargets" TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "OrderDispatches" TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT ON "OrganizationGrants" TO {SecurityTestContainerFixture.TenantScopedRole};
                    """);
            }

            _databaseConnectionString = connectionString;
            return connectionString;
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    private static async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var connection = new NpgsqlConnection(await EnsureDatabaseAsync());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<NpgsqlConnection> OpenScopedRoleAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(await EnsureDatabaseAsync())
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// As the table owner: five order types (levels 0, 1, 2; level 2 under an id in another letter
    /// case than its orders use; and level 7), the owner's orders of each — plus one of a type with
    /// no row, a second Execute order, and an Edit order not dispatched to the occupant — each with
    /// one target, every one but the last dispatched to the occupant organization.
    /// </summary>
    private static async Task SeedAsync()
    {
        await using var owner = await OpenOwnerAsync();
        await ExecuteAsync(owner,
            """
            DELETE FROM "ServiceOrders" WHERE "Id" LIKE 'ra-%';
            DELETE FROM "OrderTypes" WHERE lower("Id") LIKE 'rls-%';
            DELETE FROM "OrganizationGrants" WHERE "GranteeOrganizationId" = @grantee;
            INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description", "RecipientAccess") VALUES
                ('rls-inform', 'Inform', '', 0),
                ('rls-execute', 'Execute', '', 1),
                ('rls-edit', 'Edit', '', 2),
                ('RLS-Edit-Other-Case', 'Edit, other case', '', 2),
                ('rls-out-of-range', 'Out of range', '', 7);
            """,
            ("grantee", Grantee));

        var types = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Inform] = "rls-inform",
            [Execute] = "rls-execute",
            [Edit] = "rls-edit",
            [EditByOtherCase] = "rls-edit-other-case",
            [OutOfRange] = "rls-out-of-range",
            [NoTypeRow] = "rls-no-such-type",
            [SecondExecute] = "rls-execute",
            [EditNotDispatched] = "rls-edit",
        };
        foreach (var (orderId, typeId) in types)
        {
            await InsertOrderAsync(owner, orderId, Owner, typeId);
            await InsertTargetAsync(owner, $"tgt-{orderId}", orderId, $"pole-{orderId}", Owner);
            if (!string.Equals(orderId, EditNotDispatched, StringComparison.Ordinal))
            {
                await ExecuteAsync(owner,
                    """
                    INSERT INTO "OrderDispatches" ("ServiceOrderId", "TargetId", "TargetType", "DispatchedBy", "DispatchedAt", "ActorKind")
                    VALUES (@order, @target, 2, 'tester', now(), 0)
                    """,
                    ("order", orderId), ("target", Occupant.ToString("D")));
            }
        }
    }

    /// <summary>An active, unscoped, service-order grant from the owner to <see cref="Grantee"/>.</summary>
    private static async Task GrantAsync()
    {
        await using var owner = await OpenOwnerAsync();
        await ExecuteAsync(owner,
            """
            INSERT INTO "OrganizationGrants"
                ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions", "GrantedBy", "GrantedAt", "IsActive", "ExpiresAt")
            VALUES (gen_random_uuid(), @grantee, @owner, 'ServiceOrder', NULL, '', 'tester', now(), true, NULL)
            """,
            ("grantee", Grantee), ("owner", Owner));
    }

    private static Task<int> InsertOrderAsync(NpgsqlConnection connection, string orderId, Guid organizationId, string orderTypeId) =>
        ExecuteAsync(connection,
            """
            INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
            VALUES (@id, 'Order', '', @type, 'Pending', 0, 'tester', @org, now(), '{}', '[]')
            """,
            ("id", orderId), ("type", orderTypeId), ("org", organizationId));

    private static Task<int> InsertTargetAsync(NpgsqlConnection connection, string targetId, string orderId, string pole, Guid organizationId) =>
        ExecuteAsync(connection,
            """
            INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
            VALUES (@id, @order, @pole, 'pole', '[]', 'Pending', 0, now(), 'tester', @org, 0)
            """,
            ("id", targetId), ("order", orderId), ("pole", pole), ("org", organizationId));

    /// <summary>
    /// Runs <paramref name="sql"/> on <paramref name="connection"/> itself — same session, same role,
    /// same organization — and returns the affected row count.
    /// </summary>
    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var session = OnConnection(connection);
        return await session.Database.ExecuteSqlRawAsync(
            EscapeBraces(sql), parameters.Select(parameter => new NpgsqlParameter(parameter.Name, parameter.Value)));
    }

    /// <summary>EF reads raw SQL as a composite format string; the SQL here is literal, JSON braces included.</summary>
    private static string EscapeBraces(string sql) =>
        sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

    /// <summary>A context that borrows an open connection: it neither closes nor disposes it.</summary>
    private static ServiceOrderDbContext OnConnection(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(connection).Options);

    /// <summary>What accepting, annotating or editing an order does to its row, once per order: how many rows each update wrote.</summary>
    private static async Task<Dictionary<string, int>> TouchEachOrderAsync(NpgsqlConnection connection)
    {
        var updated = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var orderId in AllOrders)
        {
            updated[orderId] = await ExecuteAsync(connection,
                """UPDATE "ServiceOrders" SET "Title" = 'Edited', "UpdatedAt" = now() WHERE "Id" = @order""", ("order", orderId));
        }

        return updated;
    }

    /// <summary>What recording work does to a target, once per order: how many target rows each update wrote.</summary>
    private static async Task<Dictionary<string, int>> CompleteEachOrdersTargetsAsync(NpgsqlConnection connection)
    {
        var updated = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var orderId in AllOrders)
        {
            updated[orderId] = await ExecuteAsync(connection,
                """UPDATE "ServiceOrderTargets" SET "Status" = 'Done', "Note" = 'worked', "Version" = "Version" + 1 WHERE "ServiceOrderId" = @order""",
                ("order", orderId));
        }

        return updated;
    }

    private static async Task<string> TargetStatusAsync(string orderId)
    {
        await using var owner = await OpenOwnerAsync();
        await using var command = owner.CreateCommand();
        command.CommandText = """SELECT "Status" FROM "ServiceOrderTargets" WHERE "Id" = @target""";
        command.Parameters.Add(new NpgsqlParameter("target", $"tgt-{orderId}"));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> TargetCountAsync(string orderId)
    {
        await using var owner = await OpenOwnerAsync();
        await using var command = owner.CreateCommand();
        command.CommandText = """SELECT count(*) FROM "ServiceOrderTargets" WHERE "ServiceOrderId" = @order""";
        command.Parameters.Add(new NpgsqlParameter("order", orderId));
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ReadIdsAsync(NpgsqlConnection connection, string sql)
    {
        await using var session = OnConnection(connection);
        return await session.Database.SqlQueryRaw<string>(EscapeBraces(sql)).ToListAsync();
    }

    /// <summary><c>level ≥ 0:level ≥ 1:level ≥ 2</c> of the access function for one order, as the session's organization.</summary>
    private static async Task<string> AccessAsync(NpgsqlConnection connection, string orderId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT service_order_dispatched_to_current_organization_with_access(@order, 0)::text || ':'
                || service_order_dispatched_to_current_organization_with_access(@order, 1)::text || ':'
                || service_order_dispatched_to_current_organization_with_access(@order, 2)::text
            """;
        command.Parameters.Add(new NpgsqlParameter("order", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = orderId });
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> StaysAsync(NpgsqlConnection connection, string targetId, string orderId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT service_order_target_stays_on_order(@target, @order)";
        command.Parameters.Add(new NpgsqlParameter("target", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = targetId });
        command.Parameters.Add(new NpgsqlParameter("order", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = orderId });
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
