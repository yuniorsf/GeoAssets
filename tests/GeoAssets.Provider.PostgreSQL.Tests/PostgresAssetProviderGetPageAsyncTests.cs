using System.Data.Common;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    private async Task SeedAsync(string sql)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    /// <summary>
    /// A fresh table has no planner statistics, so Postgres may under/over-estimate selectivity
    /// and pick a sequential scan regardless of which indexes exist — ANALYZE is required for the
    /// index-usage assertions below to reflect steady-state behavior, not a cold-cache fluke.
    /// </summary>
    private async Task AnalyzeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("ANALYZE geo_entity; ANALYZE geo_entity_attribute;");
    }

    /// <summary>
    /// Captures every <see cref="DbCommand"/> EF Core sends, text *and* bound parameters —
    /// <see cref="CommandCapturingInterceptor"/> only keeps text, which isn't enough to
    /// replay a command (e.g. the array parameter a large <c>IN</c>/<c>ANY</c> clause binds)
    /// as a real, parameterized <c>EXPLAIN</c> below (XD01-171).
    /// </summary>
    private sealed class CapturingInterceptor : DbCommandInterceptor
    {
        public List<CapturedCommand> Captured { get; } = [];

        private void Capture(DbCommand command)
        {
            var clonedParams = command.Parameters
                .Cast<NpgsqlParameter>()
                .Select(p => new NpgsqlParameter { ParameterName = p.ParameterName, Value = p.Value, NpgsqlDbType = p.NpgsqlDbType })
                .ToList();
            Captured.Add(new CapturedCommand { CommandText = command.CommandText, Parameters = clonedParams });
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CapturedCommand
    {
        public required string CommandText { get; init; }
        public required List<NpgsqlParameter> Parameters { get; init; }
    }

    /// <summary>
    /// Runs <c>EXPLAIN (ANALYZE, BUFFERS)</c> on the real SQL <see cref="PostgresAssetProvider.GetPageAsync"/>
    /// sent (captured via <see cref="CapturingInterceptor"/>), with its real bound parameters
    /// cloned onto a fresh command — never hand-inlined as SQL literals — so the plan reflects
    /// what actually runs in production, not a hand-written approximation (XD01-171).
    /// </summary>
    private async Task<string> ExplainCapturedAsync(CapturedCommand cmd)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var explainCmd = connection.CreateCommand();
        explainCmd.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + cmd.CommandText;
        foreach (var p in cmd.Parameters)
            explainCmd.Parameters.Add(new NpgsqlParameter { ParameterName = p.ParameterName, Value = p.Value, NpgsqlDbType = p.NpgsqlDbType });

        var lines = new List<string>();
        await using var reader = await explainCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
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
            INSERT INTO geo_entity_attribute ("EntityId", "Key", "Value")
            VALUES
                ('a', 'voltage', '220V'),
                ('c', 'material', 'steel');
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
            INSERT INTO geo_entity_attribute ("EntityId", "Key", "Value")
            VALUES
                ('a', 'voltage', '220V'),
                ('c', 'material', 'steel');
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

        var interceptor = new CapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;
        await using var sut = CreateSut(options);

        await sut.GetPageAsync(new AssetQuery { SearchText = "zzz-no-match" });

        foreach (var cmd in interceptor.Captured)
        {
            var plan = await ExplainCapturedAsync(cmd);
            plan.Should().NotContain("Seq Scan on geo_entity ",
                because: "a leading-wildcard ILIKE on Name/Description should use the IX_geo_entity_Name_Trgm / " +
                         "IX_geo_entity_Description_Trgm GIN indexes instead of scanning all 200,000 rows");
        }
    }

    /// <summary>
    /// XD01-171 added B-tree indexes on Name/CreatedAt/UpdatedAt so GetPageAsync's non-default
    /// SortBy paths — previously only the default Id/PK sort was index-backed — don't force a
    /// sequential scan + in-memory sort at scale.
    /// </summary>
    [Theory]
    [InlineData("name")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    public async Task GetPageAsync_NonDefaultSort_UsesBtreeIndexNotSequentialScan(string sortBy)
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            SELECT 'seed-' || gs, 'Asset ' || gs, 'type-a'
            FROM generate_series(1, 11000) AS gs;
            """);
        await AnalyzeAsync();

        var interceptor = new CapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;
        await using var sut = CreateSut(options);

        await sut.GetPageAsync(new AssetQuery { SortBy = sortBy, Take = 50 });

        // Only the paged SELECT (the one with LIMIT) is what the B-tree index actually serves —
        // the COUNT statement has no WHERE clause here (no SearchText/AssetTypeId filter), so a
        // sequential scan is the *correct* plan for it regardless of any index.
        var pagedSelect = interceptor.Captured.Should().ContainSingle(c => c.CommandText.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)).Subject;
        var plan = await ExplainCapturedAsync(pagedSelect);
        plan.Should().NotContain("Seq Scan on geo_entity ",
            because: $"sorting by '{sortBy}' should use its matching B-tree index instead of a sequential scan + sort over all 11,000 rows");
    }

    /// <summary>
    /// XD01-171 replaced the jsonb_each_text + ILIKE custom-attributes match (which sequentially
    /// scanned geo_entity on every search regardless of indexing — see the removed
    /// *_StillSequentiallyScansDespiteGinIndex test this superseded) with a correlated EXISTS
    /// against the normalized, trigram-indexed geo_entity_attribute table.
    /// </summary>
    [Fact]
    public async Task GetPageAsync_SearchTextOnCustomAttributes_UsesAttributeTrigramIndexNotSequentialScan()
    {
        // 200,000 rows, not this file's usual 10,500/11,000 scale: the same empirically-observed
        // crossover as the Name/Description trigram test above applies here — below ~150k narrow
        // rows Postgres's planner correctly prefers a sequential scan over a GIN bitmap scan, so a
        // smaller table wouldn't actually exercise the index either.
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            SELECT 'seed-' || gs, 'Asset ' || gs, 'type-a'
            FROM generate_series(1, 200000) AS gs;
            INSERT INTO geo_entity_attribute ("EntityId", "Key", "Value")
            SELECT 'seed-' || gs, 'material', 'concreto'
            FROM generate_series(1, 200000) AS gs;
            """);
        await AnalyzeAsync();

        var interceptor = new CapturingInterceptor();
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(interceptor)
            .Options;
        await using var sut = CreateSut(options);

        // Every row has a "concreto" attribute, so a search that matches nothing (rather than
        // matching 100% of rows, which would make a sequential scan the *correct* plan) is what
        // actually exercises the attribute trigram indexes — same reasoning as the
        // Name/Description test above using "zzz-no-match".
        await sut.GetPageAsync(new AssetQuery { SearchText = "zzz-no-match" });

        foreach (var cmd in interceptor.Captured)
        {
            var plan = await ExplainCapturedAsync(cmd);
            plan.Should().NotContain("Seq Scan on geo_entity ",
                because: "the custom-attributes search path should use the geo_entity_attribute trigram indexes instead of scanning geo_entity");
            plan.Should().NotContain("Seq Scan on geo_entity_attribute ",
                because: "the custom-attributes search path should use IX_geo_entity_attribute_Value_Trgm instead of scanning geo_entity_attribute");
        }
    }
}
