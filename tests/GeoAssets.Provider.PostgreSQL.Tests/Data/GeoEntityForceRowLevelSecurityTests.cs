using FluentAssertions;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests.Data;

/// <summary>
/// Proves the <c>ForceGeoEntityRowLevelSecurity</c> migration's end-to-end effect: the
/// <c>geoassets_app</c> role it creates — the role real hosts are meant to connect as, per
/// <see cref="HttpTenantContext"/> (XD01-200), not an ad hoc test-only role — is actually denied
/// cross-tenant rows, the same guarantee <see cref="GeoEntityRowLevelSecurityPolicyTests"/>
/// already proved generically via a disposable role. The container's own superuser (the table
/// owner here) always bypasses RLS regardless of FORCE, so this test deliberately targets the
/// non-owner <c>geoassets_app</c> role instead — proving "the app role (not just a
/// superuser/owner) is denied cross-tenant access," per the migration's own acceptance bar.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeoEntityForceRowLevelSecurityTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string AppRole = "geoassets_app";
    private const string AppRolePassword = "geoassets_app_test_password";

    private static readonly Guid OrgA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OrgB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public async Task InitializeAsync()
    {
        // The migration creates geoassets_app with LOGIN but deliberately no password (a
        // migration must never embed a real credential — see its own doc comment). Setting one
        // here is test-only plumbing so this suite can actually open a connection as that role;
        // it is idempotent and safe to repeat across the collection's test classes.
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync($"ALTER ROLE {AppRole} PASSWORD '{AppRolePassword}'");
    }

    public Task DisposeAsync()
    {
        // Each test opens its own NpgsqlConnection as geoassets_app, but Npgsql pools physical
        // connections keyed by connection string and does not reset session-level GUCs
        // (set_config(..., false)) on return to the pool. Without clearing the pool, a later
        // test in this class can be handed back a connection still carrying a prior test's
        // app.current_organization_id value instead of genuinely unset — same reasoning as
        // GeoEntityRowLevelSecurityPolicyTests.DisposeAsync.
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    private async Task SeedAsync(string sql)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private string BuildAppRoleConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = AppRole,
            Password = AppRolePassword,
        };
        return builder.ConnectionString;
    }

    private static async Task<List<string>> ReadGeoEntityIdsAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"Id\" FROM geo_entity ORDER BY \"Id\"";

        var ids = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    [Fact]
    public async Task SelectAsGeoassetsAppRole_WithSessionOrganizationSet_ReturnsOnlyThatOrganizationsRows()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES
                ('force-a', 'Asset A', 'type-a', '{OrgA}'),
                ('force-b', 'Asset B', 'type-a', '{OrgB}');
            """);

        await using var connection = new NpgsqlConnection(BuildAppRoleConnectionString());
        await connection.OpenAsync();

        await using (var setConfig = connection.CreateCommand())
        {
            setConfig.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
            setConfig.Parameters.Add(new NpgsqlParameter("orgId", OrgA.ToString()));
            await setConfig.ExecuteNonQueryAsync();
        }

        var ids = await ReadGeoEntityIdsAsync(connection);

        ids.Should().Equal("force-a");
    }

    [Fact]
    public async Task SelectAsGeoassetsAppRole_SessionOrganizationNeverSet_ReturnsNoRows()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES
                ('force-a', 'Asset A', 'type-a', '{OrgA}'),
                ('force-b', 'Asset B', 'type-a', '{OrgB}');
            """);

        await using var connection = new NpgsqlConnection(BuildAppRoleConnectionString());
        await connection.OpenAsync();

        var ids = await ReadGeoEntityIdsAsync(connection);

        ids.Should().BeEmpty(because: "geoassets_app is not the table owner, so FORCE ROW LEVEL SECURITY applying or not makes no difference to it — RLS already restricted it before this migration; this test exists to prove the migration's role/grants are wired correctly end to end");
    }

    [Fact]
    public async Task InsertAsGeoassetsAppRole_ForDifferentOrganizationThanSession_IsRejected()
    {
        await ClearAsync();

        await using var connection = new NpgsqlConnection(BuildAppRoleConnectionString());
        await connection.OpenAsync();

        await using (var setConfig = connection.CreateCommand())
        {
            setConfig.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
            setConfig.Parameters.Add(new NpgsqlParameter("orgId", OrgA.ToString()));
            await setConfig.ExecuteNonQueryAsync();
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES ('force-c', 'Asset C', 'type-a', @orgB)
            """;
        insert.Parameters.Add(new NpgsqlParameter("orgB", OrgB));

        var act = async () => await insert.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>(because: "the policy's WITH CHECK clause rejects inserting a row for a different organization than the session's own, even for the app role");
    }
}
