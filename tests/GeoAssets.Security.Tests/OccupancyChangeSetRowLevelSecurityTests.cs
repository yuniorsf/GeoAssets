using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Cross-tenant leakage tests for the table of staged occupancy changes (XD01-315),
/// <c>"ServiceOrderOccupancyChangeSets"</c>, against the real migrated schema — in a database of
/// this class's own, see <see cref="EnsureDatabaseAsync"/> — and as the non-owner
/// <see cref="SecurityTestContainerFixture.TenantScopedRole"/>.
///
/// The rule is owner only, like the occupancy the sets propose to change — and unlike the
/// targets they hang from: an <c>"OrganizationGrants"</c> row that shows a grantee the order and
/// its targets shows it none of the staged changes, which name the pole owner's occupants.
/// </summary>
public sealed class OccupancyChangeSetRowLevelSecurityTests
{
    private static readonly Guid Owner = Guid.Parse("e1e1e1e1-0000-0000-0000-000000000001");
    private static readonly Guid Grantee = Guid.Parse("e2e2e2e2-0000-0000-0000-000000000002");
    private static readonly Guid Occupant = Guid.Parse("e3e3e3e3-0000-0000-0000-000000000003");
    private static readonly Guid Stranger = Guid.Parse("e4e4e4e4-0000-0000-0000-000000000004");

    private const string OwnerOrder = "ocs-order-owner";
    private const string OwnerOtherOrder = "ocs-order-owner-2";
    private const string StrangerOrder = "ocs-order-stranger";
    private const string OwnerTarget = "ocs-tgt-owner";
    private const string OwnerSecondTarget = "ocs-tgt-owner-2";
    private const string OwnerOtherOrderTarget = "ocs-tgt-owner-other";
    private const string StrangerTarget = "ocs-tgt-stranger";

    [Fact]
    public async Task ChangeSets_SelectAcrossTenants_OnlyThePoleOwnerSeesItsSets_InEveryStatus()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        (await VisibleAsync(connection, Owner)).Should().Equal($"{OwnerTarget}:Discarded", $"{OwnerTarget}:Staged");
        (await VisibleAsync(connection, Stranger)).Should().Equal($"{StrangerTarget}:Staged");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ServiceOrder")]
    public async Task ChangeSets_AGranteeOfThePoleOwner_SeesTheOrderAndItsTargets_ButNoneOfTheStagedChanges(string? grantResourceType)
    {
        await SeedAsync();
        await GrantAsync(grantResourceType);
        await using var connection = await OpenScopedRoleAsync();

        (await VisibleAsync(connection, Grantee)).Should().BeEmpty("no grant widens the owner-only policy of staged changes");
        (await CountOrdersAsync(connection, OwnerOrder)).Should().Be(1, "the grant is live: it shows the grantee the order");
        (await CountTargetsAsync(connection, OwnerOrder)).Should().Be(2, "and the order's targets");
    }

    [Fact]
    public async Task ChangeSets_AGranteeWhoCanWriteTheOrdersTargets_CanNeitherStageNorChangeNorDeleteAStagedSet()
    {
        await SeedAsync();
        await GrantAsync("ServiceOrder");
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Grantee);

        var asItself = async () => await InsertAsync(connection, OwnerOrder, OwnerSecondTarget, "Staged", Grantee);
        var asTheOwner = async () => await InsertAsync(connection, OwnerOrder, OwnerSecondTarget, "Staged", Owner);

        (await asItself.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "the target is not the grantee's own");
        (await asTheOwner.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "the row would not carry the session's organization");
        (await DiscardAsync(connection, targetId: null)).Should().Be(0);
        (await DeleteAsync(connection, targetId: null)).Should().Be(0);
        (await VisibleAsync(connection, Owner)).Should().Equal($"{OwnerTarget}:Discarded", $"{OwnerTarget}:Staged");
    }

    [Fact]
    public async Task ChangeSets_AnOccupantNamedInAStagedChange_SeesNothing()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        (await VisibleAsync(connection, Occupant)).Should().BeEmpty();
        (await CountNamingAsync(connection, Occupant)).Should().Be(0, "being named in a change gives no access to it");
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        (await CountNamingAsync(connection, Occupant)).Should().Be(2, "the owner's sets do name the occupant");
    }

    [Fact]
    public async Task ChangeSets_ASetUnderItsOwnOrganizationOnAnotherOwnersTarget_ViolatesRowSecurityPolicy_SoItCannotBlockTheOwner()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Stranger);

        // The owner's second target has nothing staged: a row here would take its one staged slot.
        var squatting = async () => await InsertAsync(connection, OwnerOrder, OwnerSecondTarget, "Staged", Stranger);
        var underItsOwnOrder = async () => await InsertAsync(connection, StrangerOrder, OwnerSecondTarget, "Staged", Stranger);

        (await squatting.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await underItsOwnOrder.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        await InsertAsync(connection, OwnerOrder, OwnerSecondTarget, "Staged", Owner);
        (await VisibleAsync(connection, Owner)).Should().Contain($"{OwnerSecondTarget}:Staged");
    }

    [Fact]
    public async Task ChangeSets_ASetNamingAnotherOrderThanItsTargets_ViolatesRowSecurityPolicy_EvenForTheOwner()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);

        var wrongOrder = async () => await InsertAsync(connection, OwnerOtherOrder, OwnerSecondTarget, "Staged", Owner);

        (await wrongOrder.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task ChangeSets_UpdateAndDeleteAcrossTenants_AffectNoRows_AndASetCannotBeMovedToAnotherOrganization()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        foreach (var outsider in new[] { Grantee, Occupant })
        {
            await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, outsider);
            (await DiscardAsync(connection, targetId: null)).Should().Be(0);
            (await DeleteAsync(connection, targetId: null)).Should().Be(0);
        }

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Stranger);
        (await DiscardAsync(connection, OwnerTarget)).Should().Be(0);
        (await DeleteAsync(connection, OwnerTarget)).Should().Be(0);
        var takingOver = async () => await MoveToOrganizationAsync(connection, StrangerTarget, Owner);
        (await takingOver.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

        (await VisibleAsync(connection, Owner)).Should().Equal($"{OwnerTarget}:Discarded", $"{OwnerTarget}:Staged");
        (await VisibleAsync(connection, Stranger)).Should().Equal($"{StrangerTarget}:Staged");
    }

    [Fact]
    public async Task ChangeSets_ThePoleOwner_CanStageDiscardAndDeleteItsOwn()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);

        await InsertAsync(connection, OwnerOrder, OwnerSecondTarget, "Staged", Owner);
        var discarded = await DiscardAsync(connection, OwnerSecondTarget);
        var deleted = await DeleteAsync(connection, OwnerTarget);

        discarded.Should().Be(1);
        deleted.Should().Be(2, "its staged and its discarded set");
        (await VisibleAsync(connection, Owner)).Should().Equal($"{OwnerSecondTarget}:Discarded");
    }

    [Fact]
    public async Task ChangeSets_ASessionThatNeverSetAnOrganization_SeesNoRows()
    {
        await SeedAsync();

        // Unpooled, so this is a physical session that has never had app.current_organization_id
        // set (a pooled one may carry the empty-string remnant of an earlier, reset setting).
        var unpooled = new NpgsqlConnectionStringBuilder(await EnsureDatabaseAsync())
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
            Pooling = false,
        };
        await using var connection = new NpgsqlConnection(unpooled.ConnectionString);
        await connection.OpenAsync();

        await using var rows = connection.CreateCommand();
        rows.CommandText = """SELECT count(*) FROM "ServiceOrderOccupancyChangeSets" """;
        ((long)(await rows.ExecuteScalarAsync())!).Should().Be(0, "with no organization the policy matches nothing — it fails closed");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly SemaphoreSlim DatabaseLock = new(1, 1);
    private static string? _databaseConnectionString;

    /// <summary>
    /// A database of this class's own in the suite's container, migrated with the real chains.
    /// The other row-level-security classes share one database and empty <c>"ServiceOrders"</c>
    /// wholesale between their facts; xUnit runs classes in parallel, so orders and targets
    /// seeded there by this class could vanish mid-fact. The non-owner role is the suite's own
    /// (roles belong to the server, not to a database), granted here what it is granted there.
    /// </summary>
    private static async Task<string> EnsureDatabaseAsync()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            if (_databaseConnectionString is not null) return _databaseConnectionString;

            var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
            {
                Database = "occupancy_change_set_rls",
            }.ConnectionString;

            await using (var identity = new GeoIdentityDbContext(SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString)))
                await identity.Database.MigrateAsync();
            await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
            {
                await serviceOrders.Database.MigrateAsync();
                await serviceOrders.Database.ExecuteSqlRawAsync($"""
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderOccupancyChangeSets" TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderTargets" TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrders" TO {SecurityTestContainerFixture.TenantScopedRole};
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
    /// As the table owner: the pole owner's order with two targets — one holding a staged and a
    /// discarded set, whose changes name <see cref="Occupant"/> — a second order of the owner
    /// with one target, and the stranger's order with one target and one staged set. Clears what
    /// an earlier fact of this class left.
    /// </summary>
    private static async Task SeedAsync()
    {
        await using var owner = await OpenOwnerAsync();
        await using (var reset = owner.CreateCommand())
        {
            reset.CommandText =
                """
                DELETE FROM "ServiceOrders" WHERE "Id" IN ('ocs-order-owner', 'ocs-order-owner-2', 'ocs-order-stranger');
                DELETE FROM "OrganizationGrants" WHERE "GranteeOrganizationId" = @grantee;
                """;
            reset.Parameters.Add(new NpgsqlParameter("grantee", Grantee));
            await reset.ExecuteNonQueryAsync();
        }

        await InsertOrderAsync(owner, OwnerOrder, Owner, (OwnerTarget, "ocs-pole-1"), (OwnerSecondTarget, "ocs-pole-2"));
        await InsertOrderAsync(owner, OwnerOtherOrder, Owner, (OwnerOtherOrderTarget, "ocs-pole-3"));
        await InsertOrderAsync(owner, StrangerOrder, Stranger, (StrangerTarget, "ocs-pole-9"));
        await InsertAsync(owner, OwnerOrder, OwnerTarget, "Staged", Owner);
        await InsertAsync(owner, OwnerOrder, OwnerTarget, "Discarded", Owner);
        await InsertAsync(owner, StrangerOrder, StrangerTarget, "Staged", Stranger);
    }

    private static async Task GrantAsync(string? resourceType)
    {
        await using var owner = await OpenOwnerAsync();
        await using var grant = owner.CreateCommand();
        grant.CommandText =
            """
            INSERT INTO "OrganizationGrants"
                ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions", "GrantedBy", "GrantedAt", "IsActive", "ExpiresAt")
            VALUES (gen_random_uuid(), @grantee, @owner, @resourceType, NULL, '', 'tester', now(), true, NULL)
            """;
        grant.Parameters.Add(new NpgsqlParameter("grantee", Grantee));
        grant.Parameters.Add(new NpgsqlParameter("owner", Owner));
        grant.Parameters.Add(new NpgsqlParameter("resourceType", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = resourceType is null ? DBNull.Value : resourceType });
        await grant.ExecuteNonQueryAsync();
    }

    private static async Task InsertOrderAsync(NpgsqlConnection connection, string orderId, Guid organizationId, params (string TargetId, string Pole)[] targets)
    {
        await using (var order = connection.CreateCommand())
        {
            order.CommandText =
                """
                INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
                VALUES (@id, 'Order', '', 'inspection', 'Draft', 0, 'tester', @org, now(), '{}', '[]')
                """;
            order.Parameters.Add(new NpgsqlParameter("id", orderId));
            order.Parameters.Add(new NpgsqlParameter("org", organizationId));
            await order.ExecuteNonQueryAsync();
        }

        foreach (var (targetId, pole) in targets)
        {
            await using var target = connection.CreateCommand();
            target.CommandText =
                """
                INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
                VALUES (@id, @order, @pole, 'pole', '[]', 'Pending', 0, now(), 'tester', @org, 0)
                """;
            target.Parameters.Add(new NpgsqlParameter("id", targetId));
            target.Parameters.Add(new NpgsqlParameter("order", orderId));
            target.Parameters.Add(new NpgsqlParameter("pole", pole));
            target.Parameters.Add(new NpgsqlParameter("org", organizationId));
            await target.ExecuteNonQueryAsync();
        }
    }

    /// <summary>One change set whose single change names <see cref="Occupant"/> as the occupant organization.</summary>
    private static async Task InsertAsync(NpgsqlConnection connection, string orderId, string targetId, string status, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO "ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
            VALUES (gen_random_uuid(), @order, @target, 'ocs-pole', @changes, 0, @status, 'tester', now(), CASE WHEN @status = 'Staged' THEN NULL ELSE now() END, @org)
            """;
        insert.Parameters.Add(new NpgsqlParameter("order", orderId));
        insert.Parameters.Add(new NpgsqlParameter("target", targetId));
        insert.Parameters.Add(new NpgsqlParameter("changes", $$"""[{"kind":"Add","slotIndex":3,"entryId":"{{Guid.NewGuid()}}","status":"AuthorizedContracted","occupantOrganizationId":"{{Occupant}}"}]"""));
        insert.Parameters.Add(new NpgsqlParameter("status", status));
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>Every change set the session sees as <paramref name="organizationId"/>, as <c>target:status</c>.</summary>
    private static async Task<List<string>> VisibleAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "TargetId" || ':' || "Status" FROM "ServiceOrderOccupancyChangeSets" WHERE "TargetId" LIKE 'ocs-%' ORDER BY 1""";
        var lines = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return lines;
    }

    private static async Task<long> CountOrdersAsync(NpgsqlConnection connection, string orderId)
    {
        await using var count = connection.CreateCommand();
        count.CommandText = """SELECT count(*) FROM "ServiceOrders" WHERE "Id" = @order""";
        count.Parameters.Add(new NpgsqlParameter("order", orderId));
        return (long)(await count.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountTargetsAsync(NpgsqlConnection connection, string orderId)
    {
        await using var count = connection.CreateCommand();
        count.CommandText = """SELECT count(*) FROM "ServiceOrderTargets" WHERE "ServiceOrderId" = @order""";
        count.Parameters.Add(new NpgsqlParameter("order", orderId));
        return (long)(await count.ExecuteScalarAsync())!;
    }

    /// <summary>How many of this suite's sets the session sees whose changes name <paramref name="organizationId"/>.</summary>
    private static async Task<long> CountNamingAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await using var count = connection.CreateCommand();
        count.CommandText =
            """SELECT count(*) FROM "ServiceOrderOccupancyChangeSets" WHERE "TargetId" LIKE 'ocs-%' AND "ChangesJson" LIKE '%' || @organization || '%'""";
        count.Parameters.Add(new NpgsqlParameter("organization", organizationId.ToString("D")));
        return (long)(await count.ExecuteScalarAsync())!;
    }

    /// <summary>Discards the staged set of one target, or — with no target — of every target the session can reach.</summary>
    private static async Task<int> DiscardAsync(NpgsqlConnection connection, string? targetId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE "ServiceOrderOccupancyChangeSets" SET "Status" = 'Discarded', "ClosedAt" = now()
            WHERE "Status" = 'Staged' AND "TargetId" LIKE 'ocs-%' AND (@target IS NULL OR "TargetId" = @target)
            """;
        update.Parameters.Add(new NpgsqlParameter("target", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = targetId is null ? DBNull.Value : targetId });
        return await update.ExecuteNonQueryAsync();
    }

    /// <summary>Deletes the sets of one target, or — with no target — every set the session can reach.</summary>
    private static async Task<int> DeleteAsync(NpgsqlConnection connection, string? targetId)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText =
            """DELETE FROM "ServiceOrderOccupancyChangeSets" WHERE "TargetId" LIKE 'ocs-%' AND (@target IS NULL OR "TargetId" = @target)""";
        delete.Parameters.Add(new NpgsqlParameter("target", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = targetId is null ? DBNull.Value : targetId });
        return await delete.ExecuteNonQueryAsync();
    }

    private static async Task<int> MoveToOrganizationAsync(NpgsqlConnection connection, string targetId, Guid organizationId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = """UPDATE "ServiceOrderOccupancyChangeSets" SET "OrganizationId" = @organization WHERE "TargetId" = @target""";
        update.Parameters.Add(new NpgsqlParameter("organization", organizationId));
        update.Parameters.Add(new NpgsqlParameter("target", targetId));
        return await update.ExecuteNonQueryAsync();
    }
}
