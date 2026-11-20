using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>
/// One lazily-started Postgres Testcontainer for the suite (never connects anywhere else), holding a template database that
/// carries both migration chains the NeutralOperator host owns — the host's <c>ServiceOrderDbContext</c> chain (assembly
/// <c>GeoAssets.NeutralOperator.Server</c>), then <c>NeutralOperatorDbContext</c>'s — and handing each test a copy of it, so a
/// test starts on a fully migrated database of its own in milliseconds. The container is torn down with the test process.
/// </summary>
internal static class NeutralOperatorDatabase
{
    private const string TemplateName = "no_template";

    private static readonly SemaphoreSlim _startLock = new(1, 1);
    private static PostgreSqlContainer? _container;

    /// <summary>A new, fully migrated database; returns its connection string.</summary>
    public static async Task<string> CreateMigratedAsync()
    {
        var (container, admin) = await EnsureTemplateAsync();
        var name = $"no_{Guid.NewGuid():N}";
        var result = await container.ExecScriptAsync($"""CREATE DATABASE "{name}" TEMPLATE "{TemplateName}";""");
        if (result.ExitCode != 0) throw new InvalidOperationException($"Creating database {name} failed: {result.Stderr}");

        return WithDatabase(admin, name);
    }

    /// <summary>The connection string of a database that does not exist yet — the migrator creates it, as it would on a brand-new environment.</summary>
    public static async Task<string> NewNameOnEmptyServerAsync()
    {
        var (_, admin) = await EnsureTemplateAsync();
        return WithDatabase(admin, $"no_empty_{Guid.NewGuid():N}");
    }

    /// <summary>The host's own <c>ServiceOrderDbContext</c>, built the way the host builds it, over <paramref name="connectionString"/>.</summary>
    public static ServiceOrderDbContext ServiceOrderContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(NeutralOperatorServerPersistence.MigrationsAssembly))
            .Options);

    /// <summary>The same <c>ServiceOrderDbContext</c> with the migration chain of the <c>GeoAssets.FieldService.Server</c> host — the other isolated vertical database, the reference the isolation SQL of this host's chain claims to reproduce.</summary>
    public static ServiceOrderDbContext FieldServiceChainContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.FieldService.Server"))
            .Options);

    /// <summary>The vertical's <c>NeutralOperatorDbContext</c> over <paramref name="connectionString"/>, with its migrations assembly and history table.</summary>
    public static NeutralOperatorDbContext VerticalContext(string connectionString) =>
        new(new DbContextOptionsBuilder<NeutralOperatorDbContext>()
            .UseNpgsql(connectionString, NeutralOperatorDbContext.ConfigureMigrations)
            .Options);

    public static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database, Pooling = false }.ConnectionString;

    private static async Task<(PostgreSqlContainer Container, string Admin)> EnsureTemplateAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            if (_container is not null) return (_container, AdminConnectionString(_container));

            var container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase("postgres")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await container.StartAsync();

            var admin = AdminConnectionString(container);
            var template = WithDatabase(admin, TemplateName);
            await using (var serviceOrders = ServiceOrderContext(template))
                await serviceOrders.Database.MigrateAsync();
            await using (var vertical = VerticalContext(template))
                await vertical.Database.MigrateAsync();

            _container = container;
            return (container, admin);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private static string AdminConnectionString(PostgreSqlContainer container) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = "postgres", Pooling = false }.ConnectionString;
}
