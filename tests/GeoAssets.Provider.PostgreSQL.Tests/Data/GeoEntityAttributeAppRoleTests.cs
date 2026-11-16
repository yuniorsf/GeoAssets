using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests.Data;

/// <summary>
/// Proves the <c>GrantGeoEntityAttributeToAppRole</c> migration (XD01-308): the non-owner
/// <c>geoassets_app</c> role — the one a tenant-isolated host connects as — can run the
/// attribute lookups that <c>identifier-list</c> selection depends on, and the policy the same
/// migration puts on <c>geo_entity_attribute</c> shows that role only the attributes of features
/// its tenant can see. Before the migration every statement here was refused with 42501.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeoEntityAttributeAppRoleTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string AppRole = "geoassets_app";

    // Same value the other app-role test classes of this collection set.
    private const string AppRolePassword = "geoassets_app_test_password";

    private const string GrantMigration = "20261116210000_GrantGeoEntityAttributeToAppRole";
    private const string PreviousMigration = "20261102122602_AddAppliedMutationBatchesRowLevelSecurity";

    private static readonly Guid OrgA = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000308");
    private static readonly Guid OrgB = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000308");
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly AssetSpatialFilter Unfiltered = new(null, null, int.MaxValue);

    public async Task InitializeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync($"ALTER ROLE {AppRole} PASSWORD '{AppRolePassword}'");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    public Task DisposeAsync()
    {
        // Pooled app-role connections may carry a session-level organization; drop them.
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    private string AppRoleConnectionString => new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
    {
        Username = AppRole,
        Password = AppRolePassword,
    }.ConnectionString;

    private DbContextOptions<GeoAssetsDbContext> AppRoleOptions => new DbContextOptionsBuilder<GeoAssetsDbContext>()
        .UseNpgsql(AppRoleConnectionString, npgsql => npgsql.UseNetTopologySuite())
        .Options;

    private static GeoFeature Pole(string id, Guid organizationId, string plate) => new()
    {
        Id = id,
        Geometry = new GeoPoint(1, 1),
        Properties = new GeoFeatureProperties
        {
            Name = id,
            AssetTypeId = "type-pole",
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = plate },
        },
    };

    /// <summary>Seeds through the real provider (as the owner), so <c>geo_entity_attribute</c> is filled as in production.</summary>
    private async Task SeedTwoOrganizationsAsync()
    {
        var options = fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        provider.AddRange(
        [
            Pole("a-1", OrgA, "P-100"),
            Pole("a-2", OrgA, "P-200"),
            Pole("b-1", OrgB, "P-100"),
            Pole("b-2", OrgB, "P-300"),
        ]);
    }

    private async Task<NpgsqlConnection> OpenAsAppRoleAsync(Guid? organizationId = null)
    {
        var connection = new NpgsqlConnection(AppRoleConnectionString);
        await connection.OpenAsync();
        if (organizationId is { } id)
        {
            await using var stamp = connection.CreateCommand();
            stamp.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
            stamp.Parameters.Add(new NpgsqlParameter("orgId", id.ToString()));
            await stamp.ExecuteNonQueryAsync();
        }

        return connection;
    }

    private static async Task<List<string>> AttributeOwnersAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "EntityId" FROM geo_entity_attribute ORDER BY "EntityId" """;
        var ids = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    [Fact]
    public async Task IdentifierLookup_AsTheNonOwnerAppRole_FindsOnlyTheStampedTenantsFeaturesByAttribute()
    {
        await SeedTwoOrganizationsAsync();

        var forA = await new PostgresAssetSpatialQuery(AppRoleOptions, new FakeTenantContext(OrgA))
            .FindByAttributeValuesAsync("plate", ["P-100", "P-200", "P-300"], Unfiltered, CancellationToken.None);
        var forB = await new PostgresAssetSpatialQuery(AppRoleOptions, new FakeTenantContext(OrgB))
            .FindByAttributeValuesAsync("plate", ["P-100", "P-200", "P-300"], Unfiltered, CancellationToken.None);

        forA.Select(f => f.Id).Should().Equal("a-1", "a-2");
        forB.Select(f => f.Id).Should().Equal("b-1", "b-2");
    }

    [Fact]
    public async Task AttributeRows_AsTheNonOwnerAppRole_AreVisibleOnlyForFeaturesTheTenantCanSee()
    {
        await SeedTwoOrganizationsAsync();

        await using (var unstamped = await OpenAsAppRoleAsync())
            (await AttributeOwnersAsync(unstamped)).Should().BeEmpty("no organization is stamped, so no parent feature is visible");

        await using var asA = await OpenAsAppRoleAsync(OrgA);
        (await AttributeOwnersAsync(asA)).Should().Equal("a-1", "a-2");
    }

    [Fact]
    public async Task AttributeRows_AsTheNonOwnerAppRole_CannotBeWrittenForAnotherTenantsFeature()
    {
        await SeedTwoOrganizationsAsync();
        await using var asA = await OpenAsAppRoleAsync(OrgA);

        await using var own = asA.CreateCommand();
        own.CommandText = """INSERT INTO geo_entity_attribute ("EntityId", "Key", "Value") VALUES ('a-1', 'circuit', 'C-1')""";
        (await own.ExecuteNonQueryAsync()).Should().Be(1);

        await using var foreign = asA.CreateCommand();
        foreign.CommandText = """INSERT INTO geo_entity_attribute ("EntityId", "Key", "Value") VALUES ('b-1', 'circuit', 'C-1')""";
        var act = () => foreign.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Migration_GrantsAndPolicy_AreRemovedByDownAndRestoredByUp()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        var migrator = db.GetService<IMigrator>();

        (await StateAsync(db)).Should().Be((true, true, 1));

        try
        {
            await migrator.MigrateAsync(PreviousMigration);
            (await StateAsync(db)).Should().Be((false, false, 0));
        }
        finally
        {
            await migrator.MigrateAsync(GrantMigration);
        }

        (await StateAsync(db)).Should().Be((true, true, 1));
    }

    private static async Task<(bool CanSelect, bool RowSecurity, int Policies)> StateAsync(GeoAssetsDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT has_table_privilege('{AppRole}', 'geo_entity_attribute', 'SELECT'),
                   (SELECT relrowsecurity FROM pg_class WHERE relname = 'geo_entity_attribute'),
                   (SELECT count(*)::int FROM pg_policies WHERE tablename = 'geo_entity_attribute')
            """;
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetBoolean(0), reader.GetBoolean(1), reader.GetInt32(2));
    }
}
