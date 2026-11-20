using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// One lazily started PostgreSQL container for the XD01-395 classes that need the real provider (same pattern as
/// <see cref="IpBillingMigrationChainTests"/>): each test migrates its own, freshly-named database inside it, and the
/// container is torn down with the test process. A Testcontainers port is random — never the local compose stack's.
/// </summary>
internal static class IpBillingPostgres
{
    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;

    public static string NewDatabaseName() => $"ipbilling_grant_{Guid.NewGuid():N}";

    /// <summary>A context on a database that does not exist yet; <c>MigrateAsync</c> creates it.</summary>
    public static Task<IpBillingDbContext> NewContextOnNewDatabaseAsync() =>
        NewContextAsync(NewDatabaseName());

    public static async Task<IpBillingDbContext> NewContextAsync(string database)
    {
        var container = await EnsureContainerAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database }.ConnectionString;
        var options = new DbContextOptionsBuilder<IpBillingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("GeoAssets.IpBilling.Server"))
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
                .WithDatabase("geoassets_ipbilling_grant_sync_test")
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
