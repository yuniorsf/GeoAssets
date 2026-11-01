using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeoAssets.Security.Tests;

/// <summary>
/// One real Postgres+PostGIS container, shared across this project's test classes via a static,
/// lazily-started instance (same pattern as <c>ServiceOrdersRowLevelSecurityPolicyTests</c> in
/// GeoAssets.Workflow.EFCore.Tests, rather than xUnit's <c>ICollectionFixture</c>, which requires
/// a <c>public</c> type — conflicting with this project's CA1515 "make it internal"), with the
/// <b>actual</b> production EF Core migrations applied for both <see cref="GeoAssetsDbContext"/>
/// (owns <c>geo_entity</c>) and <see cref="ServiceOrderDbContext"/> (owns <c>ServiceOrders</c> and
/// <c>ContractorUsLedgerEntries</c> — its real migrations live in the <c>GeoAssets.Server</c>
/// assembly, not this one, since <c>Program.cs</c> sets <c>MigrationsAssembly("GeoAssets.Server")</c>).
///
/// Deliberately uses <see cref="DbContext.Database"/>'s <c>MigrateAsync</c> rather than
/// <c>EnsureCreatedAsync</c> plus hand-copied RLS SQL (the pattern every single-table RLS test
/// elsewhere in this repo uses): this suite's whole point is to prove the RLS protecting
/// production tables is actually wired up through real migrations, so a dropped or misconfigured
/// RLS migration must fail this suite, not be silently reapplied by the test itself (XD01-204's
/// AC: "Suite fails loudly if any RLS policy is dropped, misconfigured...").
///
/// The container is intentionally never disposed — it's torn down with the test process, same as
/// <c>ServiceOrdersRowLevelSecurityPolicyTests</c>' own container.
/// </summary>
internal static class SecurityTestContainerFixture
{
    public const string TenantScopedRole = "security_suite_scoped_role";
    public const string TenantScopedRolePassword = "security_suite_scoped_role";

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static string? _connectionString;

    public static async Task<string> EnsureStartedAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_connectionString is not null) return _connectionString;

            _container = new PostgreSqlBuilder("postgis/postgis:16-3.4")
                .WithDatabase("geoassets_security_test")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await _container.StartAsync();
            var connectionString = _container.GetConnectionString();

            await using var geoAssets = new GeoAssetsDbContext(BuildGeoAssetsOptions(connectionString));
            await geoAssets.Database.MigrateAsync();

            // ServiceOrders' real RLS policy's EXISTS clause references "OrganizationGrants"
            // (GeoIdentityDbContext), so that table must exist too before ServiceOrders'
            // migrations can run.
            await using var identity = new GeoIdentityDbContext(BuildGeoIdentityOptions(connectionString));
            await identity.Database.MigrateAsync();

            await using var serviceOrders = new ServiceOrderDbContext(BuildServiceOrderOptions(connectionString));
            await serviceOrders.Database.MigrateAsync();

            // A single non-owner role, granted access to every table this suite exercises. RLS
            // doesn't restrict a table's owner unless FORCE ROW LEVEL SECURITY is also set
            // (deliberately not set yet on any of these tables — see each RLS migration's own
            // doc comment), so a second, non-owner role is required to actually observe the
            // policies.
            await geoAssets.Database.ExecuteSqlRawAsync($"""
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{TenantScopedRole}') THEN
                      CREATE ROLE {TenantScopedRole} LOGIN PASSWORD '{TenantScopedRolePassword}';
                   END IF;
                END $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON geo_entity TO {TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrders" TO {TenantScopedRole};
                GRANT SELECT, INSERT, UPDATE, DELETE ON "ContractorUsLedgerEntries" TO {TenantScopedRole};
                -- ServiceOrders' RLS policy's EXISTS clause reads "OrganizationGrants" (its
                -- grant-aware exception) — Postgres checks SELECT privilege on every table a
                -- policy references, even for rows that end up not matching.
                GRANT SELECT ON "OrganizationGrants" TO {TenantScopedRole};
                """);

            _connectionString = connectionString;
            return _connectionString;
        }
        finally
        {
            StartLock.Release();
        }
    }

    public static DbContextOptions<GeoAssetsDbContext> BuildGeoAssetsOptions(string connectionString) =>
        new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

    public static DbContextOptions<GeoIdentityDbContext> BuildGeoIdentityOptions(string connectionString) =>
        new DbContextOptionsBuilder<GeoIdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server"))
            .Options;

    public static DbContextOptions<ServiceOrderDbContext> BuildServiceOrderOptions(string connectionString) =>
        new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server"))
            .Options;

    public static async Task<NpgsqlConnection> OpenScopedRoleConnectionAsync()
    {
        var connectionString = await EnsureStartedAsync();
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = TenantScopedRole,
            Password = TenantScopedRolePassword,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static async Task<NpgsqlConnection> OpenOwnerConnectionAsync()
    {
        var connection = new NpgsqlConnection(await EnsureStartedAsync());
        await connection.OpenAsync();
        return connection;
    }

    public static async Task SetSessionOrganizationAsync(NpgsqlConnection connection, Guid organizationId)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var setConfig = connection.CreateCommand();
        setConfig.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
        setConfig.Parameters.Add(new NpgsqlParameter("orgId", organizationId.ToString()));
        await setConfig.ExecuteNonQueryAsync();
    }
}
