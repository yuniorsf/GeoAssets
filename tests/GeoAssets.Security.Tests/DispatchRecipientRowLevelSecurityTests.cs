using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Cross-tenant tests for the dispatch-recipient row-level security of XD01-316, against the
/// real migrated <c>GeoAssets.Server</c> schema — in a database of this class's own, see
/// <see cref="EnsureDatabaseAsync"/> — and as the non-owner
/// <see cref="SecurityTestContainerFixture.TenantScopedRole"/>.
///
/// A shared-use order is fanned out into one child order per occupant organization. The child
/// stays the pole owner's and is dispatched to its occupant. These tests pin what that occupant
/// can do in the database: read the order dispatched to it, its dispatches and its targets;
/// and — its order being of a type that only informs it (XD01-333) — nothing else — not the parent, not another
/// occupant's child, not a target, not a dispatch row, not the occupancy behind it. And that
/// nobody else gained anything.
/// </summary>
public sealed class DispatchRecipientRowLevelSecurityTests
{
    private static readonly Guid Owner = Guid.Parse("d1d1d1d1-0000-0000-0000-000000000001");
    private static readonly Guid OccupantA = Guid.Parse("d2d2d2d2-0000-0000-0000-000000000002");
    private static readonly Guid OccupantB = Guid.Parse("d3d3d3d3-0000-0000-0000-000000000003");
    private static readonly Guid Grantee = Guid.Parse("d4d4d4d4-0000-0000-0000-000000000004");
    private static readonly Guid Stranger = Guid.Parse("d5d5d5d5-0000-0000-0000-000000000005");

    private const string Parent = "dr-parent";
    private const string ChildA = "dr-child-a";
    private const string ChildB = "dr-child-b";
    private const string ToUser = "dr-to-user";
    private const string ToGroup = "dr-to-group";
    private const string StrangerOrder = "dr-stranger";
    private const string ChildATarget = "dr-tgt-dr-child-a";

    private const int UserTarget = 0;
    private const int GroupTarget = 1;
    private const int OrganizationTarget = 2;

    // ── T-SEC-DISP-01..03: what a recipient reads ─────────────────────────────

    [Fact]
    public async Task Orders_ARecipientOrganization_SeesExactlyTheOrdersDispatchedToIt_NotTheParent_NotAnotherOccupants()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        (await VisibleOrdersAsync(connection, OccupantA)).Should().Equal([ChildA], "its own child; not the parent, not occupant B's child");
        (await VisibleOrdersAsync(connection, OccupantB)).Should().Equal(ChildB);
        (await VisibleOrdersAsync(connection, Owner)).Should().Equal(ChildA, ChildB, Parent, ToGroup, ToUser);
        (await VisibleOrdersAsync(connection, Stranger)).Should().Equal(StrangerOrder);
    }

    [Fact]
    public async Task Orders_ADispatchToAUserOrAGroupSpelledLikeAnOrganizationId_OpensNothing()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        (await VisibleOrdersAsync(connection, OccupantA)).Should().NotContain([ToUser, ToGroup],
            "only a dispatch to the organization is a database-level grant; user and group dispatches stay inside the owner's organization");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Orders_TheRecipientIsRecognizedWhateverLetterCaseItsIdWasWrittenIn(bool upperCase)
    {
        await SeedAsync();
        await using (var owner = await OpenOwnerAsync())
        {
            var spelled = upperCase ? Stranger.ToString("D").ToUpperInvariant() : Stranger.ToString("D");
            await InsertDispatchAsync(owner, Parent, spelled, OrganizationTarget);
        }

        await using var connection = await OpenScopedRoleAsync();

        (await VisibleOrdersAsync(connection, Stranger)).Should().Equal(Parent, StrangerOrder);
    }

    [Fact]
    public async Task Orders_AnOrganizationDispatchWhoseTargetIsNotAUuid_MatchesNobody_AndBreaksNoQuery()
    {
        await SeedAsync();
        await using (var owner = await OpenOwnerAsync())
        {
            await InsertDispatchAsync(owner, Parent, "crew-7", OrganizationTarget);
            await InsertDispatchAsync(owner, Parent, string.Empty, OrganizationTarget);
        }

        await using var connection = await OpenScopedRoleAsync();

        (await VisibleOrdersAsync(connection, OccupantA)).Should().Equal(ChildA);
        (await VisibleOrdersAsync(connection, Stranger)).Should().Equal(StrangerOrder);
    }

    [Fact]
    public async Task Orders_ASessionThatNeverSetAnOrganization_SeesNoOrderTargetOrDispatch()
    {
        await SeedAsync();
        var unpooled = new NpgsqlConnectionStringBuilder(await EnsureDatabaseAsync())
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
            Pooling = false,
        };
        await using var connection = new NpgsqlConnection(unpooled.ConnectionString);
        await connection.OpenAsync();

        (await CountAsync(connection, Table.Orders)).Should().Be(0);
        (await CountAsync(connection, Table.Targets)).Should().Be(0);
        (await CountAsync(connection, Table.Dispatches)).Should().Be(0);
    }

    // ── The order itself: update yes, relabel no, insert and delete no ────────

    [Fact]
    public async Task Orders_ARecipientOfAnInformOrder_CanNeitherUpdateNorInsertNorDeleteOne()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, OccupantA);

        var accepted = await TouchOrderAsync(connection, ChildA);
        var onParent = await TouchOrderAsync(connection, Parent);
        var onOthers = await TouchOrderAsync(connection, ChildB);
        var deleted = await DeleteOrderAsync(connection, ChildA);
        var insertUnderOwner = async () => await InsertOrderAsync(connection, "dr-forged", Owner);

        accepted.Should().Be(0, "XD01-333: the order's type has no row in \"OrderTypes\", so it only informs its recipient");
        onParent.Should().Be(0);
        onOthers.Should().Be(0);
        deleted.Should().Be(0, "no policy lets a recipient delete");
        (await insertUnderOwner.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await VisibleOrdersAsync(connection, Owner)).Should().Contain(ChildA);
    }

    [Fact]
    public async Task Orders_NobodyChangesAnOrdersOrganization_NotTheRecipient_NotAGrantee_NotEvenTheOwner()
    {
        await SeedAsync();
        await GrantAsync();
        await using var connection = await OpenScopedRoleAsync();

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, OccupantA);
        (await RelabelAsync(connection, ChildA, OccupantA)).Should().Be(
            0, "XD01-333: an Inform recipient updates no order at all; a recipient of an Edit order is refused by RecipientAccessRowLevelSecurityTests' facts");

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Grantee);
        var granteeSteals = async () => await RelabelAsync(connection, Parent, Grantee);
        (await granteeSteals.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await TouchOrderAsync(connection, Parent)).Should().Be(1, "a grantee still writes a granted order");

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        var ownerGivesAway = async () => await RelabelAsync(connection, Parent, Stranger);
        (await ownerGivesAway.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await RelabelAsync(connection, Parent, Owner)).Should().Be(1, "stating the organization an order already has changes nothing and is allowed");
        (await TouchOrderAsync(connection, Parent)).Should().Be(1);

        (await VisibleOrdersAsync(connection, Owner)).Should().Equal(ChildA, ChildB, Parent, ToGroup, ToUser);
    }

    // ── Targets: read-only for the recipient ──────────────────────────────────

    [Fact]
    public async Task Targets_ARecipient_ReadsTheTargetsOfTheOrderDispatchedToIt_AndOfNoOther()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        (await VisibleTargetsAsync(connection, OccupantA)).Should().Equal($"{ChildA}:dr-pole-1");
        (await VisibleTargetsAsync(connection, OccupantB)).Should().Equal($"{ChildB}:dr-pole-2");
        (await VisibleTargetsAsync(connection, Owner)).Should().Equal(
            $"{ChildA}:dr-pole-1", $"{ChildB}:dr-pole-2", $"{Parent}:dr-pole-1", $"{Parent}:dr-pole-2");
        (await VisibleTargetsAsync(connection, Stranger)).Should().Equal($"{StrangerOrder}:dr-pole-9");
    }

    [Fact]
    public async Task Targets_ARecipient_CanNeitherAddNorChangeNorRemoveATargetOfTheOrderDispatchedToIt()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, OccupantA);

        var addUnderOwner = async () => await InsertTargetAsync(connection, "dr-tgt-forged", ChildA, "dr-pole-3", Owner);
        var addUnderItself = async () => await InsertTargetAsync(connection, "dr-tgt-forged", ChildA, "dr-pole-3", OccupantA);
        var updated = await CompleteTargetsAsync(connection, ChildA);
        var deleted = await DeleteTargetsAsync(connection, ChildA);

        (await addUnderOwner.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await addUnderItself.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        updated.Should().Be(0);
        deleted.Should().Be(0);
        (await VisibleTargetsAsync(connection, OccupantA)).Should().Equal([$"{ChildA}:dr-pole-1"], "it still reads them");
    }

    [Fact]
    public async Task Targets_TheOwnerAndAGrantee_StillWriteThem_AsBeforeThisPolicy()
    {
        await SeedAsync();
        await GrantAsync();
        await using var connection = await OpenScopedRoleAsync();

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        await InsertTargetAsync(connection, "dr-tgt-owner-new", ToUser, "dr-pole-4", Owner);
        (await CompleteTargetsAsync(connection, ToUser)).Should().Be(1);

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Grantee);
        await InsertTargetAsync(connection, "dr-tgt-grantee-new", ToGroup, "dr-pole-5", Owner);
        var underItsOwnName = async () => await InsertTargetAsync(connection, "dr-tgt-grantee-own", ToGroup, "dr-pole-6", Grantee);
        (await underItsOwnName.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be("42501", "a target carries its order's organization, whoever writes it");
        (await CompleteTargetsAsync(connection, ToGroup)).Should().Be(1, "a grantee updates the targets of a granted order");
        (await DeleteTargetsAsync(connection, ToUser)).Should().Be(1, "and removes them, the owner's included");
        (await DeleteTargetsAsync(connection, ToGroup)).Should().Be(1);
        (await VisibleTargetsAsync(connection, Grantee)).Should().Equal(
            $"{ChildA}:dr-pole-1", $"{ChildB}:dr-pole-2", $"{Parent}:dr-pole-1", $"{Parent}:dr-pole-2");
    }

    // ── Dispatch rows: who may write the thing that now grants access ─────────

    [Fact]
    public async Task Dispatches_ARecipient_ReadsTheDispatchesOfItsOrder_AndCanPassTheOrderOnToNobody()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, OccupantA);

        var toAThirdOrganization = async () => await InsertDispatchAsync(connection, ChildA, Stranger.ToString("D"), OrganizationTarget);
        var toItsOwnCrew = async () => await InsertDispatchAsync(connection, ChildA, "occupant-crew", UserTarget);
        var redirected = await RedirectDispatchesAsync(connection, ChildA, Stranger.ToString("D"));
        var deleted = await DeleteDispatchesAsync(connection, ChildA);

        (await VisibleDispatchesAsync(connection, OccupantA)).Should().Equal($"{ChildA}:{OccupantA:D}:2");
        (await toAThirdOrganization.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await toItsOwnCrew.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        redirected.Should().Be(0);
        deleted.Should().Be(0);
        (await VisibleOrdersAsync(connection, Stranger)).Should().Equal(StrangerOrder);
    }

    [Fact]
    public async Task Dispatches_NobodyCanDispatchAnotherOrganizationsOrderToItself()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Stranger);

        var forgeOnTheParent = async () => await InsertDispatchAsync(connection, Parent, Stranger.ToString("D"), OrganizationTarget);
        var forgeOnAChild = async () => await InsertDispatchAsync(connection, ChildA, Stranger.ToString("D"), OrganizationTarget);

        (await forgeOnTheParent.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await forgeOnAChild.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await VisibleOrdersAsync(connection, Stranger)).Should().Equal([StrangerOrder], "it still sees only its own order");
        (await VisibleDispatchesAsync(connection, Stranger)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatches_TheOwnerAndAGrantee_DispatchAnOrder_AndTheNewRecipientThenSeesIt()
    {
        await SeedAsync();
        await GrantAsync();
        await using var connection = await OpenScopedRoleAsync();

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        await InsertDispatchAsync(connection, ToUser, OccupantB.ToString("D"), OrganizationTarget);
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Grantee);
        await InsertDispatchAsync(connection, ToGroup, OccupantB.ToString("D"), OrganizationTarget);

        (await VisibleOrdersAsync(connection, OccupantB)).Should().Equal(ChildB, ToGroup, ToUser);
        (await VisibleDispatchesAsync(connection, Owner)).Should().HaveCount(6, "the owner reads every dispatch of its orders");
        (await VisibleDispatchesAsync(connection, Grantee)).Should().HaveCount(6, "and so does a grantee of the owner");
    }

    // ── What stays closed ─────────────────────────────────────────────────────

    [Fact]
    public async Task Occupancy_AndStagedChanges_OfThePolesOfAnOrderDispatchedToAnOccupant_StayThePoleOwnersAlone()
    {
        await SeedAsync();
        await using (var owner = await OpenOwnerAsync())
        {
            await SeedOccupancyAsync(owner);
            await InsertChangeSetAsync(owner, "Staged", Owner);
        }

        await using var connection = await OpenScopedRoleAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, OccupantA);

        (await CountAsync(connection, Table.Occupancies)).Should().Be(0);
        (await CountAsync(connection, Table.OccupancyEntries)).Should().Be(0, "not even the entry that names it");
        (await CountAsync(connection, Table.ChangeSets)).Should().Be(0);
        var stage = async () => await InsertChangeSetAsync(connection, "Discarded", OccupantA);
        (await stage.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        (await CountAsync(connection, Table.OccupancyEntries)).Should().Be(1, "the owner does see it");
    }

    [Fact]
    public async Task TheThreeFunctions_AnswerOnlyAboutTheSessionsOwnOrganization()
    {
        await SeedAsync();
        await using var connection = await OpenScopedRoleAsync();

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, OccupantA);
        (await FlagsAsync(connection, ChildA)).Should().Be("true:false", "dispatched to it, neither owned by nor granted to it");
        (await FlagsAsync(connection, ChildB)).Should().Be("false:false");
        (await FlagsAsync(connection, Parent)).Should().Be("false:false");
        (await FlagsAsync(connection, "dr-no-such-order")).Should().Be("false:false");

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        (await FlagsAsync(connection, ChildA)).Should().Be("false:true");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly SemaphoreSlim DatabaseLock = new(1, 1);
    private static string? _databaseConnectionString;

    /// <summary>
    /// A database of this class's own in the suite's container, migrated with the real chains.
    /// The other row-level-security classes share one database and empty <c>"ServiceOrders"</c>
    /// wholesale between their facts; xUnit runs classes in parallel, so orders seeded there by
    /// this class could vanish mid-fact. The non-owner role is the suite's own (roles belong to
    /// the server, not to a database), granted here the tables these tests touch.
    /// </summary>
    private static async Task<string> EnsureDatabaseAsync()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            if (_databaseConnectionString is not null) return _databaseConnectionString;

            var connectionString = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
            {
                Database = "dispatch_recipient_rls",
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
                    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON field_pole_occupancies TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON field_pole_occupancy_entries TO {SecurityTestContainerFixture.TenantScopedRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrderOccupancyChangeSets" TO {SecurityTestContainerFixture.TenantScopedRole};
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
    /// As the table owner: the pole owner's parent order on two poles; one child per occupant,
    /// each on that occupant's pole and dispatched to it; two more orders of the owner dispatched
    /// to a user and to a group whose ids are spelled exactly like occupant A's organization id;
    /// and the stranger's own order. Clears what an earlier fact of this class left.
    /// </summary>
    private static async Task SeedAsync()
    {
        await using var owner = await OpenOwnerAsync();
        await using (var reset = owner.CreateCommand())
        {
            reset.CommandText =
                """
                DELETE FROM "ServiceOrderOccupancyChangeSets";
                DELETE FROM field_pole_occupancies;
                DELETE FROM "ServiceOrders" WHERE "Id" LIKE 'dr-%';
                DELETE FROM "OrganizationGrants" WHERE "GranteeOrganizationId" = @grantee;
                """;
            reset.Parameters.Add(new NpgsqlParameter("grantee", Grantee));
            await reset.ExecuteNonQueryAsync();
        }

        await InsertOrderAsync(owner, Parent, Owner);
        await InsertOrderAsync(owner, ChildA, Owner, Parent);
        await InsertOrderAsync(owner, ChildB, Owner, Parent);
        await InsertOrderAsync(owner, ToUser, Owner);
        await InsertOrderAsync(owner, ToGroup, Owner);
        await InsertOrderAsync(owner, StrangerOrder, Stranger);

        await InsertTargetAsync(owner, $"dr-tgt-{Parent}-1", Parent, "dr-pole-1", Owner);
        await InsertTargetAsync(owner, $"dr-tgt-{Parent}-2", Parent, "dr-pole-2", Owner);
        await InsertTargetAsync(owner, ChildATarget, ChildA, "dr-pole-1", Owner);
        await InsertTargetAsync(owner, $"dr-tgt-{ChildB}", ChildB, "dr-pole-2", Owner);
        await InsertTargetAsync(owner, $"dr-tgt-{StrangerOrder}", StrangerOrder, "dr-pole-9", Stranger);

        await InsertDispatchAsync(owner, ChildA, OccupantA.ToString("D"), OrganizationTarget);
        await InsertDispatchAsync(owner, ChildB, OccupantB.ToString("D"), OrganizationTarget);
        await InsertDispatchAsync(owner, ToUser, OccupantA.ToString("D"), UserTarget);
        await InsertDispatchAsync(owner, ToGroup, OccupantA.ToString("D"), GroupTarget);
    }

    /// <summary>An active, unscoped, service-order grant from the pole owner to <see cref="Grantee"/>.</summary>
    private static async Task GrantAsync()
    {
        await using var owner = await OpenOwnerAsync();
        await using var grant = owner.CreateCommand();
        grant.CommandText =
            """
            INSERT INTO "OrganizationGrants"
                ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions", "GrantedBy", "GrantedAt", "IsActive", "ExpiresAt")
            VALUES (gen_random_uuid(), @grantee, @owner, 'ServiceOrder', NULL, '', 'tester', now(), true, NULL)
            """;
        grant.Parameters.Add(new NpgsqlParameter("grantee", Grantee));
        grant.Parameters.Add(new NpgsqlParameter("owner", Owner));
        await grant.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertOrderAsync(NpgsqlConnection connection, string orderId, Guid organizationId, string? parentOrderId = null)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson", "ParentOrderId")
            VALUES (@id, 'Order', '', 'inspection', 'Draft', 0, 'tester', @org, now(), '{}', '[]', @parent)
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", orderId));
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        insert.Parameters.Add(new NpgsqlParameter("parent", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = parentOrderId is null ? DBNull.Value : parentOrderId });
        return await insert.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertTargetAsync(NpgsqlConnection connection, string targetId, string orderId, string pole, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
            VALUES (@id, @order, @pole, 'pole', '[]', 'Pending', 0, now(), 'tester', @org, 0)
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", targetId));
        insert.Parameters.Add(new NpgsqlParameter("order", orderId));
        insert.Parameters.Add(new NpgsqlParameter("pole", pole));
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        return await insert.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertDispatchAsync(NpgsqlConnection connection, string orderId, string targetId, int targetType)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO "OrderDispatches" ("ServiceOrderId", "TargetId", "TargetType", "DispatchedBy", "DispatchedAt", "ActorKind")
            VALUES (@order, @target, @type, 'tester', now(), 0)
            """;
        insert.Parameters.Add(new NpgsqlParameter("order", orderId));
        insert.Parameters.Add(new NpgsqlParameter("target", targetId));
        insert.Parameters.Add(new NpgsqlParameter("type", targetType));
        return await insert.ExecuteNonQueryAsync();
    }

    /// <summary>Occupancy on file for the pole of occupant A's child: one contracted entry naming occupant A.</summary>
    private static async Task SeedOccupancyAsync(NpgsqlConnection connection)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO field_pole_occupancies ("Id", "PoleFeatureId", "OrganizationId", "Version", "UpdatedAt")
            VALUES ('0c000000-0000-0000-0000-000000000001', 'dr-pole-1', @owner, 1, now());
            INSERT INTO field_pole_occupancy_entries ("Id", "PoleOccupancyId", "SlotIndex", "Status", "OccupantOrganizationId", "CableDiameterMm", "CableLoadDaN", "OrganizationId")
            VALUES (gen_random_uuid(), '0c000000-0000-0000-0000-000000000001', 1, 'AuthorizedContracted', @occupant, 12.5, 0, @owner);
            """;
        insert.Parameters.Add(new NpgsqlParameter("owner", Owner));
        insert.Parameters.Add(new NpgsqlParameter("occupant", OccupantA));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>A change set on the target of occupant A's child, under <paramref name="organizationId"/>.</summary>
    private static async Task<int> InsertChangeSetAsync(NpgsqlConnection connection, string status, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO "ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
            VALUES (gen_random_uuid(), @order, @target, 'dr-pole-1', '[]', 1, @status, 'tester', now(), CASE WHEN @status = 'Staged' THEN NULL ELSE now() END, @org)
            """;
        insert.Parameters.Add(new NpgsqlParameter("order", ChildA));
        insert.Parameters.Add(new NpgsqlParameter("target", ChildATarget));
        insert.Parameters.Add(new NpgsqlParameter("status", status));
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        return await insert.ExecuteNonQueryAsync();
    }

    /// <summary>What accepting or annotating an order does to its row: assigns and stamps it.</summary>
    private static async Task<int> TouchOrderAsync(NpgsqlConnection connection, string orderId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = """UPDATE "ServiceOrders" SET "AssignedTo" = 'crew-1', "UpdatedAt" = now() WHERE "Id" = @order""";
        update.Parameters.Add(new NpgsqlParameter("order", orderId));
        return await update.ExecuteNonQueryAsync();
    }

    private static async Task<int> RelabelAsync(NpgsqlConnection connection, string orderId, Guid organizationId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = """UPDATE "ServiceOrders" SET "OrganizationId" = @org WHERE "Id" = @order""";
        update.Parameters.Add(new NpgsqlParameter("org", organizationId));
        update.Parameters.Add(new NpgsqlParameter("order", orderId));
        return await update.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteOrderAsync(NpgsqlConnection connection, string orderId)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText = """DELETE FROM "ServiceOrders" WHERE "Id" = @order""";
        delete.Parameters.Add(new NpgsqlParameter("order", orderId));
        return await delete.ExecuteNonQueryAsync();
    }

    private static async Task<int> CompleteTargetsAsync(NpgsqlConnection connection, string orderId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = """UPDATE "ServiceOrderTargets" SET "Status" = 'Done' WHERE "ServiceOrderId" = @order""";
        update.Parameters.Add(new NpgsqlParameter("order", orderId));
        return await update.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteTargetsAsync(NpgsqlConnection connection, string orderId)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText = """DELETE FROM "ServiceOrderTargets" WHERE "ServiceOrderId" = @order""";
        delete.Parameters.Add(new NpgsqlParameter("order", orderId));
        return await delete.ExecuteNonQueryAsync();
    }

    private static async Task<int> RedirectDispatchesAsync(NpgsqlConnection connection, string orderId, string targetId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = """UPDATE "OrderDispatches" SET "TargetId" = @target WHERE "ServiceOrderId" = @order""";
        update.Parameters.Add(new NpgsqlParameter("target", targetId));
        update.Parameters.Add(new NpgsqlParameter("order", orderId));
        return await update.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteDispatchesAsync(NpgsqlConnection connection, string orderId)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText = """DELETE FROM "OrderDispatches" WHERE "ServiceOrderId" = @order""";
        delete.Parameters.Add(new NpgsqlParameter("order", orderId));
        return await delete.ExecuteNonQueryAsync();
    }

    private enum Table
    {
        Orders,
        Targets,
        Dispatches,
        Occupancies,
        OccupancyEntries,
        ChangeSets,
    }

    /// <summary>How many rows of one table the session sees, with whatever organization it has set.</summary>
    private static async Task<long> CountAsync(NpgsqlConnection connection, Table table)
    {
        await using var count = connection.CreateCommand();
        switch (table)
        {
            case Table.Orders: count.CommandText = """SELECT count(*) FROM "ServiceOrders" """; break;
            case Table.Targets: count.CommandText = """SELECT count(*) FROM "ServiceOrderTargets" """; break;
            case Table.Dispatches: count.CommandText = """SELECT count(*) FROM "OrderDispatches" """; break;
            case Table.Occupancies: count.CommandText = """SELECT count(*) FROM field_pole_occupancies"""; break;
            case Table.OccupancyEntries: count.CommandText = """SELECT count(*) FROM field_pole_occupancy_entries"""; break;
            default: count.CommandText = """SELECT count(*) FROM "ServiceOrderOccupancyChangeSets" """; break;
        }

        return (long)(await count.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ReadAsync(NpgsqlCommand command)
    {
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return lines;
    }

    /// <summary>The ids of this suite's orders the session sees as <paramref name="organizationId"/>.</summary>
    private static async Task<List<string>> VisibleOrdersAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ServiceOrders" WHERE "Id" LIKE 'dr-%' ORDER BY "Id" COLLATE "C" """;
        return await ReadAsync(select);
    }

    /// <summary>This suite's targets the session sees, as <c>order:pole</c>.</summary>
    private static async Task<List<string>> VisibleTargetsAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText =
            """SELECT "ServiceOrderId" || ':' || "FeatureId" FROM "ServiceOrderTargets" WHERE "ServiceOrderId" LIKE 'dr-%' ORDER BY ("ServiceOrderId" || ':' || "FeatureId") COLLATE "C" """;
        return await ReadAsync(select);
    }

    /// <summary>This suite's dispatch rows the session sees, as <c>order:target:type</c>.</summary>
    private static async Task<List<string>> VisibleDispatchesAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText =
            """SELECT "ServiceOrderId" || ':' || "TargetId" || ':' || "TargetType"::text FROM "OrderDispatches" WHERE "ServiceOrderId" LIKE 'dr-%' ORDER BY ("ServiceOrderId" || ':' || "TargetId") COLLATE "C" """;
        return await ReadAsync(select);
    }

    /// <summary><c>dispatched-to-me:owned-or-granted</c> for one order, as the session's organization.</summary>
    private static async Task<string> FlagsAsync(NpgsqlConnection connection, string orderId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT service_order_dispatched_to_current_organization(@order)::text || ':'
                || service_order_owned_by_or_granted_to_current_organization(@order)::text
            """;
        command.Parameters.Add(new NpgsqlParameter("order", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = orderId });
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
