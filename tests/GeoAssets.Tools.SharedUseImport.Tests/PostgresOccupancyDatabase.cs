using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GeoAssets.Tools.SharedUseImport.Tests;

/// <summary>
/// One PostgreSQL container for this test project, started on first use and shared through a
/// static (the pattern of GeoAssets.Workflow.EFCore.Tests' <c>PostgresTargetStoreDatabase</c>).
/// The schema comes from the EF model (<c>EnsureCreated</c>); the occupancy migrations and their
/// row-level security are exercised by GeoAssets.Security.Tests.
/// </summary>
internal static class PostgresOccupancyDatabase
{
    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static string? _connectionString;

    public static async Task<string> EnsureCreatedAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_connectionString is not null) return _connectionString;

            var container = new PostgreSqlBuilder("postgres:16")
                .WithDatabase("geoassets_shared_use_import_test")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await container.StartAsync();

            var connectionString = container.GetConnectionString();
            await using (var db = new ServiceOrderDbContext(new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(connectionString).Options))
                await db.Database.EnsureCreatedAsync();

            _connectionString = connectionString;
            return connectionString;
        }
        finally
        {
            StartLock.Release();
        }
    }
}
