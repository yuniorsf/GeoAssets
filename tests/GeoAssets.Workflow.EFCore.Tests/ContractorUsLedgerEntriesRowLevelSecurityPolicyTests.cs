using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Proves the database-level half of ContractorUsLedgerEntries tenant isolation — the native
/// Row-Level Security policy added by the <c>AddContractorUsLedgerEntriesRowLevelSecurity</c>
/// migration — independently of <see cref="ServiceOrderDbContext"/>'s EF Core global query
/// filter. Equality only, no grant-based exception (contrast with
/// <see cref="ServiceOrdersRowLevelSecurityPolicyTests"/>), matching XD01-202's AC.
///
/// Mirrors that test's own pattern: the container's own role owns the table, and RLS doesn't
/// restrict owners without FORCE (deliberately not set yet — see the migration), so a second,
/// non-owner role is required to actually observe the policy applying.
/// </summary>
public sealed class ContractorUsLedgerEntriesRowLevelSecurityPolicyTests : IAsyncLifetime
{
    private const string ScopedRole = "contractor_us_ledger_scoped_role";
    private const string ScopedRolePassword = "contractor_us_ledger_scoped_role";

    private static readonly Guid ContractorA = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ContractorB = Guid.Parse("99999999-9999-9999-9999-999999999999");

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

            // EnsureCreatedAsync (not MigrateAsync) — same reasoning as
            // ServiceOrdersRowLevelSecurityPolicyTests: this test project has no
            // ProjectReference to GeoAssets.Server, where the real migrations live.
            await db.Database.EnsureCreatedAsync();

            // Duplicated verbatim from AddContractorUsLedgerEntriesRowLevelSecurity.Up().
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "ContractorUsLedgerEntries" ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON "ContractorUsLedgerEntries"
                    AS PERMISSIVE
                    FOR ALL
                    USING ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid)
                    WITH CHECK ("OrganizationId" = current_setting('app.current_organization_id', true)::uuid);
                """);

            await db.Database.ExecuteSqlRawAsync(
                $"""
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{ScopedRole}') THEN
                      CREATE ROLE {ScopedRole} LOGIN PASSWORD '{ScopedRolePassword}';
                   END IF;
                END $$;

                GRANT SELECT, INSERT, UPDATE, DELETE ON "ContractorUsLedgerEntries" TO {ScopedRole};
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
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "ContractorUsLedgerEntries" CASCADE;""");
    }

    private static async Task SeedLedgerEntryAsync(Guid id, Guid organizationId)
    {
        await using var db = new ServiceOrderDbContext(CreateOptions(await EnsureContainerAsync()));
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = """
            INSERT INTO "ContractorUsLedgerEntries"
                ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
            VALUES
                (@id, @orgId, NULL, 'US-001', 'Test entry', 1, 100, '[]', now())
            """;
        cmd.Parameters.Add(new NpgsqlParameter("id", id));
        cmd.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
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

    private static async Task<List<Guid>> ReadLedgerEntryIdsAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ContractorUsLedgerEntries" ORDER BY "Id" """;

        var ids = new List<Guid>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    [Fact]
    public async Task SelectAsScopedRole_SessionOrganizationMatchesOwner_ReturnsRow()
    {
        await ClearAsync();
        var id = Guid.NewGuid();
        await SeedLedgerEntryAsync(id, ContractorA);

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorA);

        var ids = await ReadLedgerEntryIdsAsync(connection);

        ids.Should().Equal(id);
    }

    [Fact]
    public async Task SelectAsScopedRole_DifferentContractor_ReturnsNoRows()
    {
        await ClearAsync();
        await SeedLedgerEntryAsync(Guid.NewGuid(), ContractorA);

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorB);

        var ids = await ReadLedgerEntryIdsAsync(connection);

        ids.Should().BeEmpty("no grant-based exception exists for this table — a different contractor must never see another's ledger entries");
    }

    [Fact]
    public async Task InsertAsScopedRole_OrganizationIdMatchesSession_Succeeds()
    {
        await ClearAsync();
        var id = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorA);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO "ContractorUsLedgerEntries"
                ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
            VALUES
                (@id, @orgId, NULL, 'US-002', 'Inserted under own tenant', 1, 100, '[]', now())
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("orgId", ContractorA));
        await insert.ExecuteNonQueryAsync();

        var ids = await ReadLedgerEntryIdsAsync(connection);
        ids.Should().Equal(id);
    }

    [Fact]
    public async Task InsertAsScopedRole_OrganizationIdDoesNotMatchSession_ViolatesRowSecurityPolicy()
    {
        await ClearAsync();

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorA);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO "ContractorUsLedgerEntries"
                ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
            VALUES
                (@id, @orgId, NULL, 'US-003', 'Attempted cross-tenant write', 1, 100, '[]', now())
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        insert.Parameters.Add(new NpgsqlParameter("orgId", ContractorB));

        var act = async () => await insert.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501", because: "WITH CHECK must reject a row whose OrganizationId doesn't match the session's own tenant, even via raw SQL");
    }

    [Fact]
    public async Task DeleteAsScopedRole_DifferentContractor_AffectsNoRows()
    {
        await ClearAsync();
        var id = Guid.NewGuid();
        await SeedLedgerEntryAsync(id, ContractorA);

        await using var connection = new NpgsqlConnection(await BuildScopedRoleConnectionStringAsync());
        await connection.OpenAsync();
        await SetSessionOrganizationAsync(connection, ContractorB);

        await using var delete = connection.CreateCommand();
        delete.CommandText = """DELETE FROM "ContractorUsLedgerEntries" WHERE "Id" = @id""";
        delete.Parameters.Add(new NpgsqlParameter("id", id));
        var affected = await delete.ExecuteNonQueryAsync();

        affected.Should().Be(0, "a different contractor's raw-SQL DELETE must silently affect zero rows, never the other tenant's row");
    }
}
