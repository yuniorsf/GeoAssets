using FluentAssertions;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests.Data;

/// <summary>
/// Proves the database-level half of tenant row isolation — the native Row-Level Security
/// policy added by the <c>AddGeoEntityRowLevelSecurity</c> migration — independently of
/// <see cref="GeoAssetsDbContext"/>'s EF Core global query filter (covered by
/// <see cref="GeoAssetsDbContextTenantIsolationTests"/>). The container's default role owns
/// <c>geo_entity</c>, and RLS doesn't restrict table owners unless <c>FORCE ROW LEVEL SECURITY</c>
/// is also set (deliberately not set — see the migration's doc comment), so a second, non-owner
/// role is required to actually observe the policy applying.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeoEntityRowLevelSecurityPolicyTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const string TenantScopedRole = "tenant_scoped_role";
    private const string TenantScopedRolePassword = "tenant_scoped_role";

    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrgB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync($"""
            DO $$
            BEGIN
               IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{TenantScopedRole}') THEN
                  CREATE ROLE {TenantScopedRole} LOGIN PASSWORD '{TenantScopedRolePassword}';
               END IF;
            END $$;

            GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity TO {TenantScopedRole};
            """);
    }

    public async Task DisposeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync($"""
            DROP OWNED BY {TenantScopedRole};
            DROP ROLE {TenantScopedRole};
            """);

        // tenant_scoped_role is dropped and recreated (same name, new OID) every test. Npgsql
        // pools idle physical connections keyed by connection string, so a connection opened as
        // the *previous* incarnation of this role can otherwise be handed back out of the pool
        // in a later test — its session predates the GRANT issued for the new OID, so queries on
        // it fail with "permission denied" even though the role genuinely has the privilege.
        // Clearing the pool forces the next test to open a fresh, correctly-privileged session.
        NpgsqlConnection.ClearAllPools();
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

    private string BuildTenantScopedRoleConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = TenantScopedRole,
            Password = TenantScopedRolePassword,
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
    public async Task SelectAsNonOwnerRole_WithSessionOrganizationSet_ReturnsOnlyThatOrganizationsRows()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES
                ('a', 'Asset A', 'type-a', '{OrgA}'),
                ('b', 'Asset B', 'type-a', '{OrgB}');
            """);

        await using var connection = new NpgsqlConnection(BuildTenantScopedRoleConnectionString());
        await connection.OpenAsync();

        await using (var setConfig = connection.CreateCommand())
        {
            setConfig.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
            setConfig.Parameters.Add(new NpgsqlParameter("orgId", OrgA.ToString()));
            await setConfig.ExecuteNonQueryAsync();
        }

        var ids = await ReadGeoEntityIdsAsync(connection);

        ids.Should().Equal("a");
    }

    [Fact]
    public async Task SelectAsNonOwnerRole_SessionOrganizationNeverSet_ReturnsNoRowsForAnyOrganization()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES
                ('a', 'Asset A', 'type-a', '{OrgA}'),
                ('b', 'Asset B', 'type-a', '{OrgB}');
            """);

        await using var connection = new NpgsqlConnection(BuildTenantScopedRoleConnectionString());
        await connection.OpenAsync();

        var ids = await ReadGeoEntityIdsAsync(connection);

        ids.Should().BeEmpty(because: "current_setting(..., true) returns NULL when unset, and NULL::uuid = \"OrganizationId\" is never true, so the policy fails closed rather than open");
    }
}
