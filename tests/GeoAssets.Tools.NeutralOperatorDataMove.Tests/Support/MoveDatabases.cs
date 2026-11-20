using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>
/// One lazily-started PostgreSQL Testcontainer for the whole test project (never connects anywhere else), holding two template
/// databases — the kernel's (the kernel's <c>ServiceOrderDbContext</c> chain from <c>GeoAssets.Server</c>, then the vertical's
/// <c>NeutralOperatorDbContext</c> chain) and NeutralOperator.Server's (its own <c>ServiceOrderDbContext</c> chain, then the
/// vertical's) — and handing each test a copy of the one it needs, so a test starts on a migrated database of its own in
/// milliseconds. The container is torn down with the test process.
/// </summary>
internal static class MoveDatabases
{
    public const string KernelMigrationsAssembly = "GeoAssets.Server";

    private const string KernelTemplate = "kernel_template";
    private const string TargetTemplate = "target_template";

    private static readonly SemaphoreSlim _startLock = new(1, 1);
    private static PostgreSqlContainer? _container;

    /// <summary>The container's superuser connection string to the maintenance database.</summary>
    public static async Task<string> AdminAsync() => (await EnsureTemplatesAsync().ConfigureAwait(false)).Admin;

    /// <summary>A new database shaped like the kernel's (chains applied, no data); returns its connection string.</summary>
    public static Task<string> CreateKernelAsync() => CreateFromAsync(KernelTemplate, "kernel");

    /// <summary>A new database shaped like NeutralOperator.Server's (chains applied, no data); returns its connection string.</summary>
    public static Task<string> CreateTargetAsync() => CreateFromAsync(TargetTemplate, "target");

    public static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database, Pooling = false }.ConnectionString;

    public static string WithRole(string connectionString, string username, string password) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Username = username, Password = password, Pooling = false }.ConnectionString;

    public static ServiceOrderDbContext ServiceOrderContext(string connectionString, string migrationsAssembly) =>
        new(new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssembly))
            .Options);

    public static NeutralOperatorDbContext VerticalContext(string connectionString) =>
        new(new DbContextOptionsBuilder<NeutralOperatorDbContext>()
            .UseNpgsql(connectionString, NeutralOperatorDbContext.ConfigureMigrations)
            .Options);

    private static async Task<string> CreateFromAsync(string template, string prefix)
    {
        var (_, admin) = await EnsureTemplatesAsync().ConfigureAwait(false);
        var name = $"{prefix}_{Guid.NewGuid():N}";
        await Sql.ExecAsync(admin, $"""CREATE DATABASE "{name}" TEMPLATE "{template}" """).ConfigureAwait(false);

        return WithDatabase(admin, name);
    }

    private static async Task<(PostgreSqlContainer Container, string Admin)> EnsureTemplatesAsync()
    {
        await _startLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_container is not null) return (_container, AdminConnectionString(_container));

            var container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase("postgres")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await container.StartAsync().ConfigureAwait(false);

            var admin = AdminConnectionString(container);
            var kernel = WithDatabase(admin, KernelTemplate);
            // The kernel's identity chain first: some of the ServiceOrderDbContext chain's migrations alter its tables (OrganizationGrants).
            var identity = new GeoIdentityDbContext(
                new DbContextOptionsBuilder<GeoIdentityDbContext>().UseNpgsql(kernel, npgsql => npgsql.MigrationsAssembly(KernelMigrationsAssembly)).Options);
            await using (identity.ConfigureAwait(false))
                await identity.Database.MigrateAsync().ConfigureAwait(false);
            var ctx = ServiceOrderContext(kernel, KernelMigrationsAssembly);
            await using (ctx.ConfigureAwait(false))
                await ctx.Database.MigrateAsync().ConfigureAwait(false);
            var kernelVertical = VerticalContext(kernel);
            await using (kernelVertical.ConfigureAwait(false))
                await kernelVertical.Database.MigrateAsync().ConfigureAwait(false);

            var target = WithDatabase(admin, TargetTemplate);
            var host = ServiceOrderContext(target, "GeoAssets.NeutralOperator.Server");
            await using (host.ConfigureAwait(false))
                await host.Database.MigrateAsync().ConfigureAwait(false);
            var targetVertical = VerticalContext(target);
            await using (targetVertical.ConfigureAwait(false))
                await targetVertical.Database.MigrateAsync().ConfigureAwait(false);

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
