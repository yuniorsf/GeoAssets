using FluentAssertions;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// Applies the <b>real</b> <see cref="IpBillingDbContext"/> migration chain (the one in
/// <c>GeoAssets.IpBilling.Server</c>'s MigrationsAssembly) to an empty PostgreSQL, end to end
/// (XD01-319). Every other test in this project goes through <see cref="SqliteFixture"/>'s
/// <c>EnsureCreated</c>, which builds the schema straight from the current EF model and never
/// executes a single migration — which is how a second migration re-adding a foreign key the
/// first one already creates (PostgreSQL 42710) went unnoticed.
///
/// Nothing here names a specific migration: the chain is whatever
/// <c>Database.GetMigrations()</c> discovers, so migrations added later are covered automatically.
///
/// One Postgres container is shared across all facts via a static, lazily-started instance (same
/// pattern, and same reasoning, as <c>ServiceOrdersRowLevelSecurityPolicyTests</c> in
/// GeoAssets.Workflow.EFCore.Tests); each fact migrates its own, freshly-named database inside
/// it, so facts never observe each other's schema. The container is intentionally never
/// disposed — it's torn down with the test process.
/// </summary>
public class IpBillingMigrationChainTests
{
    private const string MigrationsAssemblyName = "GeoAssets.IpBilling.Server";

    // SqlQueryRaw<string> maps a scalar result set through a column named "Value".
    private const string FaturamentoPostingsMunicipioForeignKeysSql =
        """
        SELECT c.conname AS "Value"
        FROM pg_constraint c
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
        WHERE c.contype = 'f'
          AND c.conrelid = '"FaturamentoPostings"'::regclass
          AND c.confrelid = '"Municipios"'::regclass
          AND a.attname = 'MunicipioId'
        """;

    private const string UserTablesSql =
        """
        SELECT table_name::text AS "Value"
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_type = 'BASE TABLE'
          AND table_name <> '__EFMigrationsHistory'
        """;

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;

    [Fact]
    public async Task MigrateAsync_OnEmptyPostgres_AppliesEveryMigrationInTheChain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        var chain = db.Database.GetMigrations().ToList();
        chain.Should().NotBeEmpty(
            $"the migrations must be discovered in the {MigrationsAssemblyName} assembly");
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(chain);
    }

    [Fact]
    public async Task MigrateAsync_OnEmptyPostgres_CreatesExactlyOneForeignKeyFromFaturamentoPostingsMunicipioIdToMunicipios()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        var foreignKeys = await db.Database
            .SqlQueryRaw<string>(FaturamentoPostingsMunicipioForeignKeysSql)
            .ToListAsync();

        foreignKeys.Should().Equal("FK_FaturamentoPostings_Municipios_MunicipioId");
    }

    [Fact]
    public async Task MigrateDownToZero_ThenUpAgain_LeavesNoTablesBehindAndReappliesTheWholeChain()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();

        await db.Database.MigrateAsync();
        (await QueryUserTablesAsync(db)).Should().NotBeEmpty();

        await migrator.MigrateAsync(Migration.InitialDatabase);

        (await db.Database.GetAppliedMigrationsAsync()).Should().BeEmpty();
        (await QueryUserTablesAsync(db)).Should().BeEmpty(
            "every migration's Down must undo exactly what its Up created");

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
    }

    private static Task<List<string>> QueryUserTablesAsync(IpBillingDbContext db) =>
        db.Database.SqlQueryRaw<string>(UserTablesSql).ToListAsync();

    /// <summary>
    /// A context pointed at a database that does not exist yet — <c>MigrateAsync</c> creates it,
    /// exactly as it would on a brand-new environment.
    /// </summary>
    private static async Task<IpBillingDbContext> NewContextOnEmptyDatabaseAsync()
    {
        var container = await EnsureContainerAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = $"ipbilling_chain_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<IpBillingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssemblyName))
            .Options;
        return new IpBillingDbContext(options);
    }

    private static async Task<PostgreSqlContainer> EnsureContainerAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_container is not null) return _container;

            var container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase("geoassets_ipbilling_test")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await container.StartAsync();

            _container = container;
            return _container;
        }
        finally
        {
            StartLock.Release();
        }
    }
}
