using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Proves the database-level half of ServiceOrders tenant isolation — the native Row-Level
/// Security policy added by the <c>AddServiceOrdersRowLevelSecurity</c> migration — including
/// its grant-aware exception, independently of <see cref="ServiceOrderDbContext"/>'s EF Core
/// global query filter (which only ever applies to the list/search surface; see that filter's
/// own doc comment). Mirrors <c>GeoEntityRowLevelSecurityPolicyTests</c>'s pattern in
/// GeoAssets.Provider.PostgreSQL.Tests: the container's own role owns "ServiceOrders", and RLS
/// doesn't restrict owners without FORCE (deliberately not set yet — see the migration), so a
/// second, non-owner role is required to actually observe the policy applying.
///
/// Shares one Postgres container across all facts via a static, lazily-started instance rather
/// than xUnit's <c>ICollectionFixture</c> (every other fixture in this project, e.g.
/// <see cref="SqliteFixture"/>, is <c>internal</c> and constructed directly per test —
/// <c>ICollectionFixture</c>/<c>[CollectionDefinition]</c> require a <c>public</c> type, which
/// conflicts with this project's CA1515 ("make it internal") the way it doesn't in
/// GeoAssets.Provider.PostgreSQL.Tests; avoiding the extra public type sidesteps that rather
/// than suppressing either rule). The container is intentionally never disposed — it's torn
/// down with the test process, same as any other test-only Testcontainers resource.
/// </summary>
public sealed class ServiceOrdersRowLevelSecurityPolicyTests : IAsyncLifetime
{
    private const string ScopedRole = "service_order_scoped_role";
    private const string ScopedRolePassword = "service_order_scoped_role";

    private static readonly Guid ConcessionaireOrg = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ContractorOrg = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid UnrelatedOrg = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static string? _connectionString;

    private static async Task<string> EnsureContainerAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_connectionString is not null) return _connectionString;

            _container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase("geoassets_workflow_test")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await _container.StartAsync();

            await using var db = new ServiceOrderDbContext(CreateOptions(_container.GetConnectionString()));

            // EnsureCreatedAsync (not MigrateAsync): ServiceOrderDbContext's real migrations
            // live under apps/GeoAssets.Server/Migrations (namespace GeoAssets.Server.Migrations)
            // — Program.cs sets MigrationsAssembly("GeoAssets.Server") explicitly in production —
            // and this test project deliberately has no ProjectReference to GeoAssets.Server (it
            // would pull in the entire web host's dependency graph for an EFCore-layer test).
            // EnsureCreatedAsync builds the schema straight from the current EF model instead, so
            // the AddServiceOrdersRowLevelSecurity migration's policy (pure SQL, no EF model
            // footprint — same as every other RLS migration in this repo) is applied by hand
            // below, duplicating its exact policy text rather than importing it.
            await db.Database.EnsureCreatedAsync();

            // The real "OrganizationGrants" table lives under a different DbContext
            // (GeoIdentityDbContext, GeoAssets.Identity.EFCore) that this test project has no
            // dependency on; only the columns the RLS policy's EXISTS subquery references are
            // needed here.
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "OrganizationGrants" (
                    "Id" uuid PRIMARY KEY,
                    "GranteeOrganizationId" uuid NOT NULL,
                    "ResourceOrganizationId" uuid NOT NULL,
                    "ResourceType" text NULL,
                    "IsActive" boolean NOT NULL,
                    "ExpiresAt" timestamp with time zone NULL
                );
                """);

            // Duplicated verbatim from AddServiceOrdersRowLevelSecurity.Up() — see that
            // migration for why every identifier is quoted and the rationale for each clause.
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "ServiceOrders" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ServiceOrders"
                    AS PERMISSIVE
                    FOR ALL
                    USING (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR EXISTS (
                            SELECT 1 FROM "OrganizationGrants" g
                            WHERE g."ResourceOrganizationId" = "ServiceOrders"."OrganizationId"
                              AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                              AND g."IsActive"
                              AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                              AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                        )
                    )
                    WITH CHECK (
                        "OrganizationId" = current_setting('app.current_organization_id', true)::uuid
                        OR EXISTS (
                            SELECT 1 FROM "OrganizationGrants" g
                            WHERE g."ResourceOrganizationId" = "ServiceOrders"."OrganizationId"
                              AND g."GranteeOrganizationId" = current_setting('app.current_organization_id', true)::uuid
                              AND g."IsActive"
                              AND (g."ExpiresAt" IS NULL OR g."ExpiresAt" > now())
                              AND (g."ResourceType" IS NULL OR g."ResourceType" = 'ServiceOrder')
                        )
                    );
                """);

            await db.Database.ExecuteSqlRawAsync(
                $"""
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ScopedRole}') THEN
                      CREATE ROLE {ScopedRole} LOGIN PASSWORD '{ScopedRolePassword}';
                   END IF;
                END $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON "ServiceOrders" TO {ScopedRole};
                GRANT SELECT ON "OrganizationGrants" TO {ScopedRole};
                """);

            _connectionString = _container.GetConnectionString();
            return _connectionString;
        }
        finally
        {
            StartLock.Release();
        }
    }

    private static DbContextOptions<ServiceOrderDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

    public Task InitializeAsync() => EnsureContainerAsync();

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    private static async Task ClearAsync()
    {
        await using var db = new ServiceOrderDbContext(CreateOptions(await EnsureContainerAsync()));
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "ServiceOrders" CASCADE; TRUNCATE TABLE "OrganizationGrants" CASCADE;""");
    }

    private static async Task SeedServiceOrderAsync(string id, Guid organizationId)
    {
        await using var db = new ServiceOrderDbContext(CreateOptions(await EnsureContainerAsync()));
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        // RowVersion is populated by a trigger in the real migrated schema (not present here —
        // see EnsureContainerAsync's own comment), so it's supplied explicitly to satisfy the
        // NOT NULL constraint; its value is irrelevant to the RLS policy under test.
        cmd.CommandText = """
            INSERT INTO "ServiceOrders"
                ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson", "RowVersion")
            VALUES
                (@id, 'Test Order', '', 'type-a', 'Draft', 0, 'tester', @orgId, now(), '{}', '[]', E'\x01')
            """;
        cmd.Parameters.Add(new NpgsqlParameter("id", id));
        cmd.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedGrantAsync(Guid resourceOrg, Guid granteeOrg, bool isActive, DateTime? expiresAt, string? resourceType)
    {
        await using var db = new ServiceOrderDbContext(CreateOptions(await EnsureContainerAsync()));
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = """
            INSERT INTO "OrganizationGrants" ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "IsActive", "ExpiresAt")
            VALUES (@id, @granteeOrg, @resourceOrg, @resourceType, @isActive, @expiresAt)
            """;
        cmd.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        cmd.Parameters.Add(new NpgsqlParameter("granteeOrg", granteeOrg));
        cmd.Parameters.Add(new NpgsqlParameter("resourceOrg", resourceOrg));
        cmd.Parameters.Add(new NpgsqlParameter("resourceType", resourceType is null ? DBNull.Value : resourceType));
        cmd.Parameters.Add(new NpgsqlParameter("isActive", isActive));
        cmd.Parameters.Add(new NpgsqlParameter("expiresAt", expiresAt is null ? DBNull.Value : expiresAt));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> BuildScopedRoleConnectionStringAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(await EnsureContainerAsync())
        {
            Username = ScopedRole,
            Password = ScopedRolePassword,
        };
        return builder.ConnectionString;
    }

    private static async Task SetSessionOrganizationAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await using var setConfig = connection.CreateCommand();
        setConfig.CommandText = "SELECT set_config('app.current_organization_id', @orgId, false)";
        setConfig.Parameters.Add(new NpgsqlParameter("orgId", organizationId.ToString()));
        await setConfig.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ReadServiceOrderIdsAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ServiceOrders" ORDER BY "Id" """;

        var ids = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    [Fact]
    public async Task SelectAsScopedRole_SessionOrganizationMatchesOwner_ReturnsRow()
    {
        await ClearAsync();
        await SeedServiceOrderAsync("owner-match", ConcessionaireOrg);

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ConcessionaireOrg);

        var ids = await ReadServiceOrderIdsAsync(connection);

        ids.Should().Equal("owner-match");
    }

    [Fact]
    public async Task SelectAsScopedRole_GranteeOrgWithActiveUnexpiredGrant_ReturnsRow()
    {
        await ClearAsync();
        await SeedServiceOrderAsync("grant-visible", ConcessionaireOrg);
        await SeedGrantAsync(ConcessionaireOrg, ContractorOrg, isActive: true, expiresAt: null, resourceType: null);

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorOrg);

        var ids = await ReadServiceOrderIdsAsync(connection);

        ids.Should().Equal(["grant-visible"], because: "an active, unexpired grant from the resource org to the session's own (grantee) org makes the row visible, mirroring CrossOrgGrantRule");
    }

    [Fact]
    public async Task SelectAsScopedRole_GranteeOrgWithExpiredGrant_ReturnsNoRows()
    {
        await ClearAsync();
        await SeedServiceOrderAsync("grant-expired", ConcessionaireOrg);
        await SeedGrantAsync(ConcessionaireOrg, ContractorOrg, isActive: true, expiresAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), resourceType: null);

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorOrg);

        var ids = await ReadServiceOrderIdsAsync(connection);

        ids.Should().BeEmpty(because: "an expired grant must not authorize access, same as EFOrganizationGrantRepository's own expiry filtering");
    }

    [Fact]
    public async Task SelectAsScopedRole_UnrelatedOrgWithoutGrant_ReturnsNoRows()
    {
        await ClearAsync();
        await SeedServiceOrderAsync("no-grant", ConcessionaireOrg);
        await SeedGrantAsync(ConcessionaireOrg, ContractorOrg, isActive: true, expiresAt: null, resourceType: null);

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, UnrelatedOrg);

        var ids = await ReadServiceOrderIdsAsync(connection);

        ids.Should().BeEmpty(because: "a grant naming a different grantee organization must not leak access to an unrelated org, mirroring Evaluate_GrantForDifferentResourceOrganization_DoesNotLeakAccess");
    }

    [Fact]
    public async Task SelectAsScopedRole_GranteeOrgWithGrantForDifferentResourceType_ReturnsNoRows()
    {
        await ClearAsync();
        await SeedServiceOrderAsync("wrong-resource-type", ConcessionaireOrg);
        await SeedGrantAsync(ConcessionaireOrg, ContractorOrg, isActive: true, expiresAt: null, resourceType: "GeoFeature");

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorOrg);

        var ids = await ReadServiceOrderIdsAsync(connection);

        ids.Should().BeEmpty(because: "a grant scoped to a different ResourceType must not authorize ServiceOrder access, mirroring Evaluate_GrantForDifferentResourceType_DoesNotLeakAccess");
    }
}
