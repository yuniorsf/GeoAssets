using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeoAssets.Bench.Render.Tests;

/// <summary>
/// One PostGIS container for this test project, started on first use and shared through a static (the pattern of
/// GeoAssets.Tools.SharedUseImport.Tests' <c>PostgresOccupancyDatabase</c>), with <c>GeoAssetsDbContext</c>'s whole
/// migration chain applied — the schema the tool's instructions have <c>dotnet ef database update</c> apply — and the
/// tool's dataset seeded once at <see cref="FeatureCount"/> features (enough to cross the endpoint's 10,000 cap).
/// </summary>
internal static class PostgresBenchDatabase
{
    public const int FeatureCount = 12_000;

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static string? _connectionString;

    public static async Task<string> EnsureSeededAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_connectionString is not null) return _connectionString;

            _container = new PostgreSqlBuilder("postgis/postgis:16-3.4")
                .WithDatabase("geoassets_render_bench_test")
                .WithUsername("geoassets")
                .WithPassword("geoassets")
                .Build();
            await _container.StartAsync();
            var connectionString = _container.GetConnectionString();

            await using (var db = new GeoAssetsDbContext(CreateOptions(connectionString)))
                await db.Database.MigrateAsync();

            await using (var dataSource = CreateDataSource(connectionString))
            {
                await new DatasetSeeder(dataSource, TimeProvider.System, TextWriter.Null)
                    .SeedAsync(new DatasetGenerator(DatasetGenerator.DefaultSeed), FeatureCount, CancellationToken.None);
            }

            _connectionString = connectionString;
            return connectionString;
        }
        finally
        {
            StartLock.Release();
        }
    }

    public static DbContextOptions<GeoAssetsDbContext> CreateOptions(string connectionString) =>
        new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseNetTopologySuite();
        return builder.Build();
    }
}
