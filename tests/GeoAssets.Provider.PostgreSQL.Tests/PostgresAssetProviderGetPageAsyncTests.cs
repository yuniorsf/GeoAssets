using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="PostgresAssetProvider.GetPageAsync"/> against a real PostGIS instance —
/// see XD01-115. Everything else on <see cref="PostgresAssetProvider"/> loads the whole
/// <c>geo_entity</c> table into an in-memory cache; this is the one query path (besides
/// <c>GetInBoundsAsync</c>) that must stay server-side even with a 10k+ row table.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderGetPageAsyncTests(PostgresContainerFixture fixture)
{
    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity");
    }

    private async Task SeedAsync(string sql)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    [Fact]
    public async Task GetPageAsync_With10000Rows_ReturnsRequestedPageWithoutLoadingFullTable()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            SELECT 'seed-' || gs, 'Asset ' || gs, 'type-a'
            FROM generate_series(1, 10500) AS gs;
            """);

        var interceptor = new CommandCapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;
        await using var sut = CreateSut(options);

        var result = await sut.GetPageAsync(new AssetQuery { Take = 50 });

        result.Items.Should().HaveCount(50);
        result.TotalCount.Should().Be(10_500);

        // Exactly 2 round-trips (COUNT + the paged SELECT) — a naive implementation that fell
        // back to the in-memory default (GetAll() -> Skip/Take) would instead issue one
        // unbounded SELECT that pulls all 10,000 rows into .NET before paging locally.
        interceptor.CommandTexts.Should().HaveCount(2);
        interceptor.CommandTexts.Should().Contain(sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetPageAsync_TotalCount_ReflectsFilteredCountNotJustPageSize()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            SELECT 'a-' || gs, 'Asset A ' || gs, 'type-a' FROM generate_series(1, 30) AS gs;
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            SELECT 'b-' || gs, 'Asset B ' || gs, 'type-b' FROM generate_series(1, 20) AS gs;
            """);

        await using var sut = CreateSut(fixture.CreateOptions());

        var result = await sut.GetPageAsync(new AssetQuery { AssetTypeId = "type-a", Take = 10 });

        result.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(30);
    }

    [Fact]
    public async Task GetPageAsync_SearchText_MatchesNameOrDescriptionCaseInsensitively()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description")
            VALUES
                ('a', 'Main Tower', 'type-a', ''),
                ('b', 'Bridge', 'type-a', 'Riverside crossing'),
                ('c', 'Substation', 'type-a', '');
            """);

        await using var sut = CreateSut(fixture.CreateOptions());

        var byName = await sut.GetPageAsync(new AssetQuery { SearchText = "tower" });
        byName.Items.Should().ContainSingle().Which.Id.Should().Be("a");
        byName.TotalCount.Should().Be(1);

        var byDescription = await sut.GetPageAsync(new AssetQuery { SearchText = "RIVER" });
        byDescription.Items.Should().ContainSingle().Which.Id.Should().Be("b");
    }

    [Fact]
    public async Task GetPageAsync_Skip_AdvancesPastAlreadyReturnedRows()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            VALUES
                ('a', 'Alpha', 'type-a'),
                ('b', 'Bravo', 'type-a'),
                ('c', 'Charlie', 'type-a'),
                ('d', 'Delta', 'type-a');
            """);

        await using var sut = CreateSut(fixture.CreateOptions());

        var page = await sut.GetPageAsync(new AssetQuery { SortBy = "name", Skip = 2, Take = 2 });

        page.Items.Select(f => f.Id).Should().Equal("c", "d");
        page.TotalCount.Should().Be(4);
    }
}
