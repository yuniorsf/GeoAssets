using FluentAssertions;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Cross-tenant leakage tests for the pole occupancy tables (XD01-314),
/// <c>field_pole_occupancies</c> and <c>field_pole_occupancy_entries</c>, against the real
/// migrated schema and as the non-owner <see cref="SecurityTestContainerFixture.TenantScopedRole"/>.
///
/// The rule is owner only: occupancy is the pole owner's data. Unlike <c>"ServiceOrders"</c>
/// and <c>"ServiceOrderTargets"</c>, no <c>"OrganizationGrants"</c> row opens it to a grantee,
/// and an occupant organization does not see even the entry that names it.
/// </summary>
public sealed class PoleOccupancyRowLevelSecurityTests
{
    private static readonly Guid Owner = Guid.Parse("d1d1d1d1-0000-0000-0000-000000000001");
    private static readonly Guid Grantee = Guid.Parse("d2d2d2d2-0000-0000-0000-000000000002");
    private static readonly Guid Occupant = Guid.Parse("d3d3d3d3-0000-0000-0000-000000000003");
    private static readonly Guid Stranger = Guid.Parse("d4d4d4d4-0000-0000-0000-000000000004");

    private static readonly Guid OwnerPole = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    private static readonly Guid StrangerPole = Guid.Parse("a4a4a4a4-0000-0000-0000-000000000004");

    private const string OwnerOrderId = "occ-order-owner";

    [Fact]
    public async Task PoleOccupancy_SelectAcrossTenants_OnlyThePoleOwnerSeesItsPolesAndTheirEntries()
    {
        await SeedAsync();
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();

        (await VisibleAsync(connection, Owner)).Should().Equal(
            ["pole:pole-owner", "entry:pole-owner:1", "entry:pole-owner:2", "entry:pole-owner:-", "entry:pole-owner:-"],
            "the owner sees its pole with every entry, the ones beyond the regulated slots included");
        (await VisibleAsync(connection, Stranger)).Should().Equal("pole:pole-stranger", "entry:pole-stranger:1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ServiceOrder")]
    [InlineData("PoleOccupancy")]
    public async Task PoleOccupancy_AGranteeOfThePoleOwner_SeesNoOccupancy_ThoughTheSameGrantShowsItTheOwnersOrders(string? grantResourceType)
    {
        await SeedAsync();
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var seed = owner.CreateCommand();
            seed.CommandText =
                """
                INSERT INTO "ServiceOrders" ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
                VALUES (@orderId, 'Order', '', 'inspection', 'Draft', 0, 'tester', @owner, now(), '{}', '[]');
                INSERT INTO "OrganizationGrants"
                    ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions", "GrantedBy", "GrantedAt", "IsActive", "ExpiresAt")
                VALUES (gen_random_uuid(), @grantee, @owner, @resourceType, NULL, '', 'tester', now(), true, NULL);
                """;
            seed.Parameters.Add(new NpgsqlParameter("orderId", OwnerOrderId));
            seed.Parameters.Add(new NpgsqlParameter("owner", Owner));
            seed.Parameters.Add(new NpgsqlParameter("grantee", Grantee));
            seed.Parameters.Add(new NpgsqlParameter("resourceType", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = grantResourceType is null ? DBNull.Value : grantResourceType });
            await seed.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();

        (await VisibleAsync(connection, Grantee)).Should().BeEmpty("no grant widens the owner-only occupancy policies");
        if (grantResourceType is null or "ServiceOrder")
        {
            await using var orders = connection.CreateCommand();
            orders.CommandText = """SELECT count(*) FROM "ServiceOrders" WHERE "Id" = 'occ-order-owner'""";
            (await CountAsync(orders)).Should().Be(1, "the grant itself is live: it does show the grantee the owner's order");
        }
    }

    [Fact]
    public async Task PoleOccupancy_AnOccupantNamedOnAnEntry_SeesNeitherThePoleNorItsOwnEntry()
    {
        await SeedAsync();
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();

        (await VisibleAsync(connection, Occupant)).Should().BeEmpty();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);
        await using var ownEntries = connection.CreateCommand();
        ownEntries.CommandText =
            """SELECT count(*) FROM field_pole_occupancy_entries WHERE "OccupantOrganizationId" = current_setting('app.current_organization_id', true)::uuid""";
        (await CountAsync(ownEntries)).Should().Be(0, "being the occupant on an entry gives no access to it");
    }

    [Fact]
    public async Task PoleOccupancy_InsertUnderAnotherOrganization_ViolatesRowSecurityPolicy_OnBothTables()
    {
        await SeedAsync();
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        var pole = async () => await InsertPoleAsync(connection, Guid.NewGuid(), "pole-forged", Owner);
        var entry = async () => await InsertEntryAsync(connection, OwnerPole, slotIndex: 6, Owner, Occupant);

        (await pole.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await entry.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task PoleOccupancy_AnEntryUnderItsOwnOrganizationOnAnotherOwnersPole_IsRejectedByTheForeignKey()
    {
        await SeedAsync();
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Occupant);

        // The policy alone would let a tenant write a row carrying its own organization; the
        // composite foreign key is what stops that row from hanging off someone else's pole.
        var act = async () => await InsertEntryAsync(connection, OwnerPole, slotIndex: null, Occupant, Occupant);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task PoleOccupancy_UpdateAndDeleteAcrossTenants_AffectNoRows_AndMovingARowToAnotherOrganizationIsRejected()
    {
        await SeedAsync();
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();

        foreach (var outsider in new[] { Grantee, Occupant, Stranger })
        {
            await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, outsider);
            await using var tamper = connection.CreateCommand();
            tamper.CommandText =
                """
                UPDATE field_pole_occupancies SET "Version" = 99 WHERE "Id" = @id;
                UPDATE field_pole_occupancy_entries SET "Status" = 'ClandestineUnauthorized' WHERE "PoleOccupancyId" = @id;
                DELETE FROM field_pole_occupancy_entries WHERE "PoleOccupancyId" = @id;
                DELETE FROM field_pole_occupancies WHERE "Id" = @id;
                """;
            tamper.Parameters.Add(new NpgsqlParameter("id", OwnerPole));
            (await tamper.ExecuteNonQueryAsync()).Should().Be(0, "none of the four statements may reach a row of another organization");
        }

        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Stranger);
        var giveAway = async () =>
        {
            await using var move = connection.CreateCommand();
            move.CommandText = """UPDATE field_pole_occupancies SET "OrganizationId" = @other WHERE "Id" = @id""";
            move.Parameters.Add(new NpgsqlParameter("other", Owner));
            move.Parameters.Add(new NpgsqlParameter("id", StrangerPole));
            await move.ExecuteNonQueryAsync();
        };
        (await giveAway.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().BeOneOf("42501", PostgresErrorCodes.ForeignKeyViolation);

        (await VisibleAsync(connection, Owner)).Should().HaveCount(5, "nothing of the owner's was touched");
    }

    [Fact]
    public async Task PoleOccupancy_ThePoleOwner_CanWriteItsOwnOccupancy_IncludingEntriesBeyondTheSixSlots()
    {
        await SeedAsync();
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, Owner);
        var newPole = Guid.NewGuid();

        await InsertPoleAsync(connection, newPole, "pole-owner-2", Owner);
        for (var slotIndex = 1; slotIndex <= 6; slotIndex++)
            await InsertEntryAsync(connection, newPole, slotIndex, Owner, Occupant);
        await InsertEntryAsync(connection, newPole, slotIndex: null, Owner, Occupant);
        await InsertEntryAsync(connection, newPole, slotIndex: null, Owner, occupant: null);
        await using var bump = connection.CreateCommand();
        bump.CommandText = """UPDATE field_pole_occupancies SET "Version" = 2 WHERE "Id" = @id AND "Version" = 1""";
        bump.Parameters.Add(new NpgsqlParameter("id", newPole));
        var bumped = await bump.ExecuteNonQueryAsync();
        await using var remove = connection.CreateCommand();
        remove.CommandText = """DELETE FROM field_pole_occupancy_entries WHERE "PoleOccupancyId" = @id AND "SlotIndex" IS NULL""";
        remove.Parameters.Add(new NpgsqlParameter("id", newPole));
        var removed = await remove.ExecuteNonQueryAsync();

        bumped.Should().Be(1);
        removed.Should().Be(2);
        await using var remaining = connection.CreateCommand();
        remaining.CommandText =
            """SELECT count(*) FROM field_pole_occupancy_entries e JOIN field_pole_occupancies p ON p."Id" = e."PoleOccupancyId" WHERE p."PoleFeatureId" = 'pole-owner-2'""";
        (await CountAsync(remaining)).Should().Be(6);
    }

    [Fact]
    public async Task PoleOccupancy_SessionThatNeverSetAnOrganization_SeesNoRows()
    {
        await SeedAsync();

        // Unpooled, so this is a physical session that has never had app.current_organization_id
        // set (a pooled one may carry the empty-string remnant of an earlier, reset setting).
        var unpooled = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
            Pooling = false,
        };
        await using var connection = new NpgsqlConnection(unpooled.ConnectionString);
        await connection.OpenAsync();

        await using var rows = connection.CreateCommand();
        rows.CommandText = "SELECT (SELECT count(*) FROM field_pole_occupancies) + (SELECT count(*) FROM field_pole_occupancy_entries)";
        (await CountAsync(rows)).Should().Be(0, "with no organization the policies match nothing — they fail closed");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// As the table owner: the owner's pole (two regulated entries and two beyond the slots, one
    /// of each naming <see cref="Occupant"/>) and the stranger's pole. Clears what an earlier
    /// fact of this class left, including its order and grant.
    /// </summary>
    private static async Task SeedAsync()
    {
        await using var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync();
        await using (var reset = owner.CreateCommand())
        {
            reset.CommandText =
                """
                TRUNCATE TABLE field_pole_occupancies CASCADE;
                DELETE FROM "ServiceOrders" WHERE "Id" = @orderId;
                DELETE FROM "OrganizationGrants" WHERE "GranteeOrganizationId" = @grantee;
                """;
            reset.Parameters.Add(new NpgsqlParameter("orderId", OwnerOrderId));
            reset.Parameters.Add(new NpgsqlParameter("grantee", Grantee));
            await reset.ExecuteNonQueryAsync();
        }

        await InsertPoleAsync(owner, OwnerPole, "pole-owner", Owner);
        await InsertEntryAsync(owner, OwnerPole, 1, Owner, Occupant);
        await InsertEntryAsync(owner, OwnerPole, 2, Owner, Grantee);
        await InsertEntryAsync(owner, OwnerPole, null, Owner, Occupant);
        await InsertEntryAsync(owner, OwnerPole, null, Owner, occupant: null);
        await InsertPoleAsync(owner, StrangerPole, "pole-stranger", Stranger);
        await InsertEntryAsync(owner, StrangerPole, 1, Stranger, Occupant);
    }

    private static async Task InsertPoleAsync(NpgsqlConnection connection, Guid id, string poleFeatureId, Guid organizationId)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO field_pole_occupancies ("Id", "PoleFeatureId", "OrganizationId", "Version", "UpdatedAt")
            VALUES (@id, @pole, @org, 1, now())
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("pole", poleFeatureId));
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task InsertEntryAsync(NpgsqlConnection connection, Guid poleId, int? slotIndex, Guid organizationId, Guid? occupant)
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO field_pole_occupancy_entries ("Id", "PoleOccupancyId", "SlotIndex", "Status", "OccupantOrganizationId", "CableDiameterMm", "CableLoadDaN", "OrganizationId")
            VALUES (gen_random_uuid(), @pole, @slot, 'AuthorizedContracted', @occupant, 12.5, 0, @org)
            """;
        insert.Parameters.Add(new NpgsqlParameter("pole", poleId));
        insert.Parameters.Add(new NpgsqlParameter("slot", NpgsqlTypes.NpgsqlDbType.Integer) { Value = slotIndex.HasValue ? slotIndex.Value : DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("occupant", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = occupant.HasValue ? occupant.Value : DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("org", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    /// <summary>Everything of both tables the session sees as <paramref name="organizationId"/>: poles first, then entries by slot.</summary>
    private static async Task<List<string>> VisibleAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organizationId);
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT 'pole:' || "PoleFeatureId" AS line, 0 AS kind, 0 AS slot FROM field_pole_occupancies
            UNION ALL
            SELECT 'entry:' || coalesce((SELECT p."PoleFeatureId" FROM field_pole_occupancies p WHERE p."Id" = e."PoleOccupancyId"), '?') || ':' || coalesce(e."SlotIndex"::text, '-'),
                   1, coalesce(e."SlotIndex", 99)
            FROM field_pole_occupancy_entries e
            ORDER BY kind, slot, line
            """;
        var lines = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return lines;
    }

    private static async Task<long> CountAsync(NpgsqlCommand count) => (long)(await count.ExecuteScalarAsync())!;
}
