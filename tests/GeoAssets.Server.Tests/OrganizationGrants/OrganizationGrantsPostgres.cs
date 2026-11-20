using GeoAssets.Identity.Authorization.EFCore;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// One PostgreSQL Testcontainer for the XD01-394 grant tests, lazily started and never disposed (the pattern of
/// <c>ServerHostFixture</c>); every test takes its own empty database on it, so tests neither share rows nor a migration state.
/// </summary>
internal static class OrganizationGrantsPostgres
{
    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public static async Task<string> CreateDatabaseAsync()
    {
        var container = await GetContainerAsync();
        var name = "grants_" + Guid.NewGuid().ToString("N");
        var result = await container.ExecScriptAsync($"CREATE DATABASE \"{name}\";");
        if (result.ExitCode != 0) throw new InvalidOperationException($"CREATE DATABASE failed: {result.Stderr}");
        // Unpooled: every test opens its own set of connections (the concurrency tests 16 at once) and must not leave idle ones
        // behind on the one server all the tests share.
        return container.GetConnectionString().Replace("Database=grants_admin", $"Database={name};Pooling=false", StringComparison.Ordinal);
    }

    /// <summary>An empty database migrated to the head of the kernel's <see cref="GeoIdentityDbContext"/> chain.</summary>
    public static async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await CreateDatabaseAsync();
        await using var db = NewContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static GeoIdentityDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.Server"))
            .Options);

    private static async Task<PostgreSqlContainer> GetContainerAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_container is null)
            {
                var container = new PostgreSqlBuilder("postgis/postgis:16-3.4")
                    .WithDatabase("grants_admin")
                    .WithUsername("geoassets")
                    .WithPassword("geoassets")
                    .Build();
                await container.StartAsync();
                _container = container;
            }

            return _container;
        }
        finally
        {
            StartLock.Release();
        }
    }
}
