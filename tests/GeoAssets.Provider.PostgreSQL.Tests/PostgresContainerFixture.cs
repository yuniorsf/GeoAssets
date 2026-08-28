using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Spins up a real PostGIS instance (the <c>InitialCreate</c> migration runs
/// <c>CREATE EXTENSION postgis</c>, which the plain <c>postgres</c> image can't satisfy) and
/// applies migrations once for the whole collection. Shared across the collection because
/// starting the container is the expensive part; individual tests are responsible for their
/// own row-level isolation (e.g. truncating <c>geo_entity</c> before seeding).
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .WithDatabase("geoassets_test")
        .WithUsername("geoassets")
        .WithPassword("geoassets")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public DbContextOptions<GeoAssetsDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = new GeoAssetsDbContext(CreateOptions());
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "Postgres";
}
