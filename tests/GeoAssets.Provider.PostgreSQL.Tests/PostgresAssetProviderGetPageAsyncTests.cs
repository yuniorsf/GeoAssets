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

    /// <summary>
    /// Runs <c>EXPLAIN</c> on raw SQL mirroring one of <see cref="PostgresAssetProvider.GetPageAsync"/>'s
    /// query shapes and returns the plan as plain text lines, so a test can assert on plan
    /// *quality* (index vs. sequential scan) rather than just round-trip count — XD01-171.
    /// </summary>
    private static async Task<string> ExplainAsync(GeoAssetsDbContext db, string sql)
    {
        var explainSql = "EXPLAIN " + sql;
        var lines = await db.Database.SqlQueryRaw<string>(explainSql).ToListAsync();
        return string.Join('\n', lines);
    }

    /// <summary>
    /// A fresh table has no planner statistics, so Postgres may under/over-estimate selectivity
    /// and pick a sequential scan regardless of which indexes exist — ANALYZE is required for the
    /// index-usage assertions below to reflect steady-state behavior, not a cold-cache fluke.
    /// </summary>
    private async Task AnalyzeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("ANALYZE geo_entity");
    }

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
    public async Task GetPageAsync_SearchText_MatchesCustomAttributeValue()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", custom_attributes)
            VALUES
                ('a', 'Main Tower', 'type-a', '', '{{"voltage": "220V"}}'),
                ('b', 'Bridge', 'type-a', 'Riverside crossing', '{{}}'),
                ('c', 'Substation', 'type-a', '', '{{"material": "steel"}}');
            """);

        await using var sut = CreateSut(fixture.CreateOptions());

        var result = await sut.GetPageAsync(new AssetQuery { SearchText = "220v" });

        result.Items.Should().ContainSingle().Which.Id.Should().Be("a");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPageAsync_SearchText_MatchesCustomAttributeKey()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", custom_attributes)
            VALUES
                ('a', 'Main Tower', 'type-a', '', '{{"voltage": "220V"}}'),
                ('b', 'Bridge', 'type-a', 'Riverside crossing', '{{}}'),
                ('c', 'Substation', 'type-a', '', '{{"material": "steel"}}');
            """);

        await using var sut = CreateSut(fixture.CreateOptions());

        var result = await sut.GetPageAsync(new AssetQuery { SearchText = "MATERIAL" });

        result.Items.Should().ContainSingle().Which.Id.Should().Be("c");
        result.TotalCount.Should().Be(1);
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

    /// <summary>
    /// XD01-171 added trigram GIN indexes on Name/Description specifically so GetPageAsync's
    /// SearchText path's leading-wildcard ILIKE (line ~172-174) doesn't force a sequential scan
    /// at scale. Needs a larger row count than this class's other scale tests (10,500/11,000):
    /// verified empirically against this exact schema that Postgres's planner still (correctly)
    /// prefers a sequential scan over the trigram indexes below ~150k narrow rows — the fixed
    /// overhead of a bitmap index scan only pays off once the table is big enough. 200,000 rows
    /// is comfortably past that crossover.
    /// </summary>
    [Fact]
    public async Task GetPageAsync_SearchTextIlikeOnNameOrDescription_UsesTrigramIndexNotSequentialScan()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "Description", "AssetTypeId")
            SELECT 'seed-' || gs, 'Asset ' || gs, 'Description for asset ' || gs, 'type-a'
            FROM generate_series(1, 200000) AS gs;
            """);
        await AnalyzeAsync();

        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        var plan = await ExplainAsync(db, """
            SELECT "Id" FROM geo_entity
            WHERE "Name" ILIKE '%zzz-no-match%' OR "Description" ILIKE '%zzz-no-match%'
            """);

        plan.Should().NotContain("Seq Scan on geo_entity",
            because: "a leading-wildcard ILIKE on Name/Description should use the IX_geo_entity_Name_Trgm / " +
                     "IX_geo_entity_Description_Trgm GIN indexes instead of scanning all 200,000 rows");
    }

    /// <summary>
    /// XD01-171 added B-tree indexes on Name/CreatedAt/UpdatedAt so GetPageAsync's non-default
    /// SortBy paths (line ~182-184) — previously only the default Id/PK sort was index-backed —
    /// don't force a sequential scan + in-memory sort at scale.
    /// </summary>
    [Theory]
    [InlineData("""ORDER BY "Name" LIMIT 50""")]
    [InlineData("""ORDER BY "CreatedAt" LIMIT 50""")]
    [InlineData("""ORDER BY "UpdatedAt" LIMIT 50""")]
    public async Task GetPageAsync_NonDefaultSort_UsesBtreeIndexNotSequentialScan(string orderByClause)
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            SELECT 'seed-' || gs, 'Asset ' || gs, 'type-a'
            FROM generate_series(1, 11000) AS gs;
            """);
        await AnalyzeAsync();

        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        var plan = await ExplainAsync(db, $"""SELECT "Id" FROM geo_entity {orderByClause}""");

        plan.Should().NotContain("Seq Scan on geo_entity",
            because: $"'{orderByClause}' should use its matching B-tree index instead of a sequential scan + sort over all 11,000 rows");
    }

    /// <summary>
    /// Documents a deliberate limitation left out of scope by XD01-171: the GIN index added on
    /// custom_attributes serves containment/existence operators (@>, ?), not the per-row ILIKE
    /// match GetPageAsync's SearchText path runs against jsonb_each_text's expanded rows — so
    /// this query shape still sequentially scans despite the new index. A rewrite to containment
    /// operators would be needed to fix this path; the ticket itself flagged that as a separate,
    /// larger effort. This test exists so a future change to the custom-attributes query shape
    /// that silently drops the (currently unavoidable) scan doesn't go unnoticed as a non-event —
    /// if this starts failing, it's good news, not a regression.
    /// </summary>
    [Fact]
    public async Task GetPageAsync_SearchTextOnCustomAttributes_StillSequentiallyScansDespiteGinIndex()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", custom_attributes)
            SELECT 'seed-' || gs, 'Asset ' || gs, 'type-a', '{{"voltage": "220V"}}'::jsonb
            FROM generate_series(1, 11000) AS gs;
            """);
        await AnalyzeAsync();

        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        var plan = await ExplainAsync(db, """
            SELECT DISTINCT ge."Id"
            FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
            WHERE attr.key ILIKE '%zzz-no-match%' OR attr.value ILIKE '%zzz-no-match%'
            """);

        plan.Should().Contain("Seq Scan on geo_entity");
    }
}
