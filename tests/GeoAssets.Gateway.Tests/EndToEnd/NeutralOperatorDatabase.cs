using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>
/// One lazily-started Postgres Testcontainer for this suite (never connecting anywhere else), holding a template database
/// that carries both migration chains the NeutralOperator host owns — the host's <c>ServiceOrderDbContext</c> chain (assembly
/// <c>GeoAssets.NeutralOperator.Server</c>), then <c>NeutralOperatorDbContext</c>'s — and handing each test a copy of it.
/// The same shape as <c>GeoAssets.NeutralOperator.Server.Tests</c>' support type of this name (its internals are not visible to this
/// assembly), under its namespace so the support types linked from there find it.
/// </summary>
internal static class NeutralOperatorDatabase
{
    private const string TemplateName = "no_template";
    private const string MigrationsAssembly = "GeoAssets.NeutralOperator.Server";

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

    public static ServiceOrderDbContext ServiceOrderContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssembly))
            .Options);

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
