using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-336 in the database, <c>GeoAssets.Server</c> history: the occupant of a shared-use notice
/// (a type registered only in code, whose recipient actions the migration seeded) may write the
/// notice's row exactly as accepting it (Pending → InProgress) or commenting on it (a new
/// <c>"UpdatedAt"</c>) does, and append Accept and Annotate entries to its log — and nothing else:
/// no cancel, no content or fixed-field change, no other entry, no update or delete of an entry.
/// Run against the real migrated schema, in a database of this class's own, as the non-owner
/// <see cref="SecurityTestContainerFixture.TenantScopedRole"/>. The database knows organizations,
/// not users or roles; the role half is the application's (<c>ServiceOrderRecipientActionsTests</c>,
/// <c>ServiceOrderRecipientActionsEndpointTests</c>).
/// </summary>
public sealed class RecipientActionsRowLevelSecurityTests
{
    private static readonly Guid Owner = Guid.Parse("e1e1e1e1-0000-0000-0000-000000000336");
    private static readonly Guid Occupant = Guid.Parse("e2e2e2e2-0000-0000-0000-000000000336");
    private static readonly Guid Grantee = Guid.Parse("e3e3e3e3-0000-0000-0000-000000000336");
    private static readonly Guid Stranger = Guid.Parse("e4e4e4e4-0000-0000-0000-000000000336");

    private const string Notice = "rx-notice";
    private const string NoticeByOtherCase = "rx-notice-other-case";
    private const string Inform = "rx-inform";
    private const string Edit = "rx-edit";
    private const string NoticeNotDispatched = "rx-notice-not-dispatched";

    private const int Accept = 4;
    private const int Cancel = 8;
    private const int Annotate = 9;

    private static readonly string[] AllOrders = [Notice, NoticeByOtherCase, Inform, Edit, NoticeNotDispatched];

    // ── The seed ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheMigration_SeedsTheNoticesAcceptAndAnnotate_AndAcceptsMoveFromPendingToInProgress()
    {
        await using var owner = await OpenOwnerAsync();

        (await ReadIdsAsync(owner,
            """
            SELECT ("OrderTypeId" || ':' || "Action"::text || ':' || coalesce("FromStateKey", '-') || ':' || coalesce("ToStateKey", '-'))::text AS "Value"
            FROM "OrderTypeRecipientActions" WHERE "OrderTypeId" = 'shared-use-occupant-notice' ORDER BY "Id"
            """))
            .Should().Equal(
                "shared-use-occupant-notice:4:-:-",
                "shared-use-occupant-notice:4:Pending:InProgress",
                "shared-use-occupant-notice:9:-:-");
    }

    // ── The order row ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Orders_TheOccupant_AcceptsItsNotice_OnlyFromPendingToInProgress()
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        var accepted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var orderId in AllOrders)
        {
            accepted[orderId] = await TryExecuteAsync(connection,
                """UPDATE "ServiceOrders" SET "Status" = 'InProgress', "UpdatedAt" = now() WHERE "Id" = @order""", ("order", orderId));
        }

        accepted.Should().Equal(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Notice] = 1,
            [NoticeByOtherCase] = 1,
            [Inform] = 0,
            [Edit] = 1,
            [NoticeNotDispatched] = 0,
        }, "a type matches whatever its letter case; a type with no recipient action grants no update below Edit; an Edit recipient keeps XD01-333's update");
        (await StatusAsync(Notice)).Should().Be("InProgress", "the update really was written");
    }

    [Fact]
    public async Task Orders_TheOccupant_CommentingStampsOnlyUpdatedAt()
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        (await ExecuteAsync(connection, """UPDATE "ServiceOrders" SET "UpdatedAt" = now() WHERE "Id" = @order""", ("order", Notice)))
            .Should().Be(1);
        (await ExecuteAsync(connection, """UPDATE "ServiceOrders" SET "UpdatedAt" = now() WHERE "Id" = @order""", ("order", Inform)))
            .Should().Be(0, "a type that grants its recipients nothing leaves them nothing to stamp");
    }

    public static TheoryData<string, string> ForbiddenNoticeUpdates() => new()
    {
        { "cancel", """UPDATE "ServiceOrders" SET "Status" = 'Cancelled', "UpdatedAt" = now() WHERE "Id" = @order""" },
        { "complete", """UPDATE "ServiceOrders" SET "Status" = 'Completed' WHERE "Id" = @order""" },
        { "accept and retitle", """UPDATE "ServiceOrders" SET "Status" = 'InProgress', "Title" = 'Mine now' WHERE "Id" = @order""" },
        { "retitle", """UPDATE "ServiceOrders" SET "Title" = 'Edited' WHERE "Id" = @order""" },
        { "describe", """UPDATE "ServiceOrders" SET "Description" = 'Edited' WHERE "Id" = @order""" },
        { "retype", """UPDATE "ServiceOrders" SET "OrderTypeId" = 'rlsx-edit' WHERE "Id" = @order""" },
        { "assign", """UPDATE "ServiceOrders" SET "AssignedTo" = 'occupant-crew' WHERE "Id" = @order""" },
        { "complete at", """UPDATE "ServiceOrders" SET "CompletedAt" = now() WHERE "Id" = @order""" },
        { "reparent", """UPDATE "ServiceOrders" SET "ParentOrderId" = 'rx-edit' WHERE "Id" = @order""" },
        { "attributes", """UPDATE "ServiceOrders" SET "AttributesJson" = '{"scopeId":"x"}' WHERE "Id" = @order""" },
        { "priority", """UPDATE "ServiceOrders" SET "Priority" = 3 WHERE "Id" = @order""" },
        { "relabel", """UPDATE "ServiceOrders" SET "OrganizationId" = 'e2e2e2e2-0000-0000-0000-000000000336' WHERE "Id" = @order""" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenNoticeUpdates))]
    public async Task Orders_TheOccupant_ChangingAnythingElseOfItsNotice_IsRefused(string change, string sql)
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        var update = async () => await ExecuteAsync(connection, sql, ("order", Notice));

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", change);
        (await StatusAsync(Notice)).Should().Be("Pending");
    }

    [Fact]
    public async Task Orders_TheOccupant_CannotMoveAnAcceptedNoticeAnyFurther_NorInsertOrDeleteOne()
    {
        await SeedAsync();
        await using (var owner = await OpenOwnerAsync())
            await ExecuteAsync(owner, """UPDATE "ServiceOrders" SET "Status" = 'InProgress' WHERE "Id" = @order""", ("order", Notice));
        await using var connection = await RecipientAsync();

        var complete = async () => await ExecuteAsync(connection, """UPDATE "ServiceOrders" SET "Status" = 'Completed' WHERE "Id" = @order""", ("order", Notice));
        var back = async () => await ExecuteAsync(connection, """UPDATE "ServiceOrders" SET "Status" = 'Pending' WHERE "Id" = @order""", ("order", Notice));
        var insert = async () => await InsertOrderAsync(connection, "rx-forged", Owner, "shared-use-occupant-notice");

        (await complete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await back.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "the move is from Pending to InProgress, not back");
        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await ExecuteAsync(connection, """DELETE FROM "ServiceOrders" WHERE "Id" = @order""", ("order", Notice))).Should().Be(0);
    }

    // ── The action log ────────────────────────────────────────────────────────

    [Fact]
    public async Task Logs_TheOccupant_AppendsOnlyTheActionsItsTypeGrantsIt_OrAnyOnAnEditOrder()
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        var appended = new List<string>();
        foreach (var orderId in AllOrders)
        foreach (var action in new[] { Accept, Annotate, Cancel })
        {
            appended.Add($"{orderId}:{action}:{await TryExecuteAsync(connection, InsertLogSql, ("order", orderId), ("action", action))}");
        }

        appended.Should().Equal(
            $"{Notice}:4:1", $"{Notice}:9:1", $"{Notice}:8:0",
            $"{NoticeByOtherCase}:4:1", $"{NoticeByOtherCase}:9:1", $"{NoticeByOtherCase}:8:0",
            $"{Inform}:4:0", $"{Inform}:9:0", $"{Inform}:8:0",
            $"{Edit}:4:1", $"{Edit}:9:1", $"{Edit}:8:1",
            $"{NoticeNotDispatched}:4:0", $"{NoticeNotDispatched}:9:0", $"{NoticeNotDispatched}:8:0");
    }

    [Fact]
    public async Task Logs_TheOccupant_ReadsTheLogOfItsNotice_ButNeitherUpdatesNorDeletesAnEntry()
    {
        await SeedAsync();
        await using (var owner = await OpenOwnerAsync())
            await ExecuteAsync(owner, InsertLogSql, ("order", Notice), ("action", Annotate));
        await using var connection = await RecipientAsync();

        (await ReadIdsAsync(connection, """SELECT "ServiceOrderId"::text AS "Value" FROM "OrderActionLogs" WHERE "ServiceOrderId" LIKE 'rx-%' """))
            .Should().Equal(Notice);
        (await ExecuteAsync(connection, """UPDATE "OrderActionLogs" SET "Comment" = 'rewritten' WHERE "ServiceOrderId" = @order""", ("order", Notice)))
            .Should().Be(0);
        (await ExecuteAsync(connection, """DELETE FROM "OrderActionLogs" WHERE "ServiceOrderId" = @order""", ("order", Notice)))
            .Should().Be(0);
    }

    // ── Nobody else changed ──────────────────────────────────────────────────

    [Fact]
    public async Task OwnerAndGrantee_WriteTheOrderAndItsLog_AsTheyPlease_AndAStrangerNothing()
    {
        await SeedAsync();
        await GrantAsync();
        await using var connection = await OpenScopedRoleAsync();

        foreach (var (organization, name) in new[] { (Owner, "owner"), (Grantee, "grantee") })
        {
            await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organization);
            (await ExecuteAsync(connection, """UPDATE "ServiceOrders" SET "Status" = 'Cancelled', "Title" = @name WHERE "Id" = @order""", ("name", name), ("order", Notice)))
                .Should().Be(1, name);
            (await ExecuteAsync(connection, InsertLogSql, ("order", Notice), ("action", Cancel))).Should().Be(1, name);
            (await ExecuteAsync(connection, """UPDATE "OrderActionLogs" SET "Comment" = @name WHERE "ServiceOrderId" = @order""", ("name", name), ("order", Notice)))
                .Should().BeGreaterThan(0, name);
            (await ExecuteAsync(connection, """DELETE FROM "OrderActionLogs" WHERE "ServiceOrderId" = @order""", ("order", Notice)))
                .Should().BeGreaterThan(0, name);
        }

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Stranger);
        (await TryExecuteAsync(connection, InsertLogSql, ("order", Notice), ("action", Annotate))).Should().Be(0);
        (await ExecuteAsync(connection, """UPDATE "ServiceOrders" SET "UpdatedAt" = now() WHERE "Id" = @order""", ("order", Notice))).Should().Be(0);
        (await ReadIdsAsync(connection, """SELECT "ServiceOrderId"::text AS "Value" FROM "OrderActionLogs" WHERE "ServiceOrderId" LIKE 'rx-%' """))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task TheFunctions_AnswerOnlyAboutTheSessionsOwnOrganization()
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        (await GrantedAsync(connection, Notice)).Should().Be("true:true:true:false");
        (await GrantedAsync(connection, Inform)).Should().Be("false:false:false:false");
        (await GrantedAsync(connection, NoticeNotDispatched)).Should().Be("false:false:false:false");

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        (await GrantedAsync(connection, Notice)).Should().Be("false:false:false:false", "the owner is not a recipient; its own policies let it write");
    }

    // ── The action codes (XD01-366) ───────────────────────────────────────────

    private const string NumberOnlyGrantType = "rlsx-number-only";
    private const string NumberOnlyGrantOrder = "rx-number-only";
    private const string NewVerbType = "rlsx-new-verb";
    private const string NewVerbOrder = "rx-new-verb";

    private const string InsertCodedLogSql =
        """
        INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "ActionCode", "PerformedBy", "PerformedAt", "Comment", "ActorKind")
        VALUES (@order, @action, @code, 'tester', now(), 'a comment', 0)
        """;

    private const string InsertCodeOnlyLogSql =
        """
        INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "ActionCode", "PerformedBy", "PerformedAt", "Comment", "ActorKind")
        VALUES (@order, NULL, @code, 'tester', now(), 'a comment', 0)
        """;

    [Fact]
    public async Task RecipientInsertPolicy_CodedEntry_IsGrantedByTheSameCode()
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        (await TryExecuteAsync(connection, InsertCodedLogSql, ("order", Notice), ("action", Annotate), ("code", "Annotate"))).Should().Be(1);
        (await TryExecuteAsync(connection, InsertCodedLogSql, ("order", Notice), ("action", Accept), ("code", "Accept"))).Should().Be(1);
        (await TryExecuteAsync(connection, InsertCodedLogSql, ("order", Notice), ("action", Cancel), ("code", "Cancel")))
            .Should().Be(0, "a code the type does not grant is refused");
    }

    [Fact]
    public async Task RecipientInsertPolicy_CodedEntry_IsNotGrantedByANumberOnlyGrant()
    {
        await SeedAsync();
        await SeedGrantAsync(NumberOnlyGrantType, NumberOnlyGrantOrder, number: Annotate, code: null);
        await using var connection = await RecipientAsync();

        (await TryExecuteAsync(connection, InsertCodedLogSql, ("order", NumberOnlyGrantOrder), ("action", Annotate), ("code", "Annotate")))
            .Should().Be(0, "strict: an entry that carries a code is granted only by a grant with that code");
        (await TryExecuteAsync(connection, InsertLogSql, ("order", NumberOnlyGrantOrder), ("action", Annotate)))
            .Should().Be(1, "the same grant still admits an entry with no code, by number");
    }

    [Fact]
    public async Task RecipientInsertPolicy_CodedEntry_ANumberAndACodeCannotBeMixedToPassAGrant()
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        (await TryExecuteAsync(connection, InsertCodedLogSql, ("order", Notice), ("action", Annotate), ("code", "Cancel")))
            .Should().Be(0, "the code governs: a granted number does not carry a code that is not granted");
        (await TryExecuteAsync(connection, InsertCodedLogSql, ("order", Notice), ("action", Cancel), ("code", "Annotate")))
            .Should().Be(1, "and the code alone is enough, whatever number rides with it");
    }

    [Fact]
    public async Task RecipientInsertPolicy_UncodedEntry_IsGrantedByNumber()
    {
        await SeedAsync();
        await using var connection = await RecipientAsync();

        (await TryExecuteAsync(connection, InsertLogSql, ("order", Notice), ("action", Annotate))).Should().Be(1);
        (await TryExecuteAsync(connection, InsertLogSql, ("order", Notice), ("action", Accept))).Should().Be(1);
        (await TryExecuteAsync(connection, InsertLogSql, ("order", Notice), ("action", Cancel))).Should().Be(0);
        (await GrantedAsync(connection, Notice)).Should().Be("true:true:true:false", "the two-argument function an older writer calls still answers");
    }

    [Fact]
    public async Task RecipientInsertPolicy_ANewVerb_IsGrantedOnlyByAGrantOfItsCode()
    {
        await SeedAsync();
        await SeedGrantAsync(NewVerbType, NewVerbOrder, number: null, code: "Survey");
        await using var connection = await RecipientAsync();

        (await TryExecuteAsync(connection, InsertCodeOnlyLogSql, ("order", NewVerbOrder), ("code", "Survey"))).Should().Be(1);
        (await TryExecuteAsync(connection, InsertCodeOnlyLogSql, ("order", NewVerbOrder), ("code", "Inspect")))
            .Should().Be(0, "another new verb is not granted");
        (await TryExecuteAsync(connection, InsertCodeOnlyLogSql, ("order", Notice), ("code", "Survey")))
            .Should().Be(0, "nor is the verb on a type that does not grant it");
    }

    /// <summary>
    /// As the table owner: an order of <paramref name="typeId"/> dispatched to the occupant organization, and the one recipient
    /// action row the type grants — <paramref name="number"/> and <paramref name="code"/> as given, either may be absent.
    /// </summary>
    private static async Task SeedGrantAsync(string typeId, string orderId, int? number, string? code)
    {
        await using var owner = await OpenOwnerAsync();
        await ExecuteAsync(owner,
            """
            DELETE FROM "OrderTypeRecipientActions" WHERE "OrderTypeId" = @type;
            INSERT INTO "OrderTypeRecipientActions" ("OrderTypeId", "Action", "ActionCode") VALUES (@type, @number, @code);
            """,
            ("type", typeId),
            ("number", number is { } n ? n : DBNull.Value),
            ("code", code is null ? DBNull.Value : code));
        await InsertOrderAsync(owner, orderId, Owner, typeId);
        await ExecuteAsync(owner,
            """
            INSERT INTO "OrderDispatches" ("ServiceOrderId", "TargetId", "TargetType", "DispatchedBy", "DispatchedAt", "ActorKind")
            VALUES (@order, @target, 2, 'tester', now(), 0)
            """,
            ("order", orderId), ("target", Occupant.ToString("D")));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string InsertLogSql =
        """
        INSERT INTO "OrderActionLogs" ("ServiceOrderId", "Action", "PerformedBy", "PerformedAt", "Comment", "ActorKind")
        VALUES (@order, @action, 'tester', now(), 'a comment', 0)
        """;

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
                Database = "recipient_actions_rls",
            }.ConnectionString;

            await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
                await identity.Database.MigrateAsync();
            await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
            {
                await serviceOrders.Database.MigrateAsync();
                await serviceOrders.Database.ExecuteSqlRawAsync($"""
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrders" TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "OrderActionLogs" TO {SecurityTestContainerFixture.TenantScopedRole};
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

    private static async Task<NpgsqlConnection> RecipientAsync()
    {
        var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);
        return connection;
    }

    /// <summary>
    /// As the table owner: an Inform type and an Edit type with rows in "OrderTypes" and no recipient
    /// action, and the owner's orders — two notices (one naming its type in another letter case), one
    /// of each stored type, and a notice not dispatched — every one but the last dispatched to the
    /// occupant organization. The notice type has no "OrderTypes" row: its actions are the seed.
    /// </summary>
    private static async Task SeedAsync()
    {
        await using var owner = await OpenOwnerAsync();
        await ExecuteAsync(owner,
            """
            DELETE FROM "ServiceOrders" WHERE "Id" LIKE 'rx-%';
            DELETE FROM "OrderTypes" WHERE "Id" LIKE 'rlsx-%';
            DELETE FROM "OrganizationGrants" WHERE "GranteeOrganizationId" = @grantee;
            INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description", "RecipientAccess") VALUES
                ('rlsx-inform', 'Inform', '', 0),
                ('rlsx-edit', 'Edit', '', 2);
            """,
            ("grantee", Grantee));

        var types = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Notice] = "shared-use-occupant-notice",
            [NoticeByOtherCase] = "Shared-Use-Occupant-Notice",
            [Inform] = "rlsx-inform",
            [Edit] = "rlsx-edit",
            [NoticeNotDispatched] = "shared-use-occupant-notice",
        };
        foreach (var (orderId, typeId) in types)
        {
            await InsertOrderAsync(owner, orderId, Owner, typeId);
            if (!string.Equals(orderId, NoticeNotDispatched, StringComparison.Ordinal))
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

    /// <summary>As <see cref="ExecuteAsync"/>, with a row-level security refusal (42501) read as 0 rows written.</summary>
    private static async Task<int> TryExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        try
        {
            return await ExecuteAsync(connection, sql, parameters);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42501", StringComparison.Ordinal))
        {
            return 0;
        }
    }

    /// <summary>EF reads raw SQL as a composite format string; the SQL here is literal, JSON braces included.</summary>
    private static string EscapeBraces(string sql) =>
        sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

    /// <summary>A context that borrows an open connection: it neither closes nor disposes it.</summary>
    private static ServiceOrderDbContext OnConnection(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(connection).Options);

    private static async Task<string> StatusAsync(string orderId)
    {
        await using var owner = await OpenOwnerAsync();
        await using var command = owner.CreateCommand();
        command.CommandText = """SELECT "Status" FROM "ServiceOrders" WHERE "Id" = @order""";
        command.Parameters.Add(new NpgsqlParameter("order", orderId));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ReadIdsAsync(NpgsqlConnection connection, string sql)
    {
        await using var session = OnConnection(connection);
        return await session.Database.SqlQueryRaw<string>(EscapeBraces(sql)).ToListAsync();
    }

    /// <summary>
    /// <c>any granted:Accept granted:Annotate granted:Cancel granted</c> for one order, as the session's organization.
    /// </summary>
    private static async Task<string> GrantedAsync(NpgsqlConnection connection, string orderId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT service_order_recipient_actions_granted(@order)::text || ':'
                || service_order_recipient_action_is_granted(@order, 4)::text || ':'
                || service_order_recipient_action_is_granted(@order, 9)::text || ':'
                || service_order_recipient_action_is_granted(@order, 8)::text
            """;
        command.Parameters.Add(new NpgsqlParameter("order", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = orderId });
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
