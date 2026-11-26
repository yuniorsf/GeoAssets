using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace GeoAssets.Bench.Render.Tests;

/// <summary>
/// The tool on a real PostGIS (Testcontainers) migrated with the kernel's chain: the seed writes the documented dataset
/// deterministically, and the reproduced bounds read answers exactly what <c>PostgresAssetProvider.GetInViewportAsync</c>
/// serialized by the endpoint answers (XD01-430) — so a change to the provider's read that the bench does not follow fails here.
/// One class: the tests share (and one re-seeds) the same table.
/// </summary>
public sealed class PostgresRenderBenchTests : IAsyncLifetime
{
    private string _connectionString = "";
    private NpgsqlDataSource? _dataSource;

    private NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("Not initialized.");

    public async Task InitializeAsync()
    {
        _connectionString = await PostgresBenchDatabase.EnsureSeededAsync();
        _dataSource = PostgresBenchDatabase.CreateDataSource(_connectionString);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null) await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task Seed_WritesTheCompositionAndEachTypesZoomRange()
    {
        var perType = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var command = DataSource.CreateCommand("""SELECT "AssetTypeId", count(*) FROM geo_entity GROUP BY "AssetTypeId" """))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) perType[reader.GetString(0)] = reader.GetInt64(1);
        }

        perType.Should().BeEquivalentTo(DatasetComposition.Counts(PostgresBenchDatabase.FeatureCount)
            .ToDictionary(c => c.Type.Id.ToString(), c => (long)c.Count, StringComparer.Ordinal));

        await using var types = DataSource.CreateCommand("""SELECT "Id", "MinZoom", "MaxZoom" FROM asset_type WHERE "Id" = ANY(@ids)""");
        types.Parameters.AddWithValue("ids", DatasetComposition.AssetTypes.Select(t => t.Id).ToArray());
        var ranges = new List<(Guid, int?, int?)>();
        await using (var reader = await types.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ranges.Add((reader.GetGuid(0), await reader.IsDBNullAsync(1) ? null : reader.GetInt32(1), await reader.IsDBNullAsync(2) ? null : reader.GetInt32(2)));
            }
        }

        ranges.Should().BeEquivalentTo(DatasetComposition.AssetTypes.Select(t => (t.Id, t.MinZoom, t.MaxZoom)));
    }

    [Fact]
    public async Task Seed_AgainWithTheSameSeed_WritesTheSameTable()
    {
        var before = await TableDigestAsync();

        var summary = await new DatasetSeeder(DataSource, TimeProvider.System, TextWriter.Null)
            .SeedAsync(new DatasetGenerator(DatasetGenerator.DefaultSeed), PostgresBenchDatabase.FeatureCount, CancellationToken.None);

        (await TableDigestAsync()).Should().Be(before);
        summary.TotalVertices.Should().Be(await ScalarAsync<long>("SELECT sum(ST_NPoints(geom))::bigint FROM geo_entity"));
    }

    [Fact]
    public async Task Seed_RefusesADatabaseWithoutTheSchema()
    {
        await using (var create = DataSource.CreateCommand("DROP DATABASE IF EXISTS geoassets_bench_unmigrated; "))
            await create.ExecuteNonQueryAsync();
        await using (var create = DataSource.CreateCommand("CREATE DATABASE geoassets_bench_unmigrated"))
            await create.ExecuteNonQueryAsync();

        var unmigrated = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "geoassets_bench_unmigrated" }.ConnectionString;
        await using var dataSource = PostgresBenchDatabase.CreateDataSource(unmigrated);
        var seed = () => new DatasetSeeder(dataSource, TimeProvider.System, TextWriter.Null)
            .SeedAsync(new DatasetGenerator(1), 10, CancellationToken.None);

        await seed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*apply GeoAssetsDbContext's migrations*");
    }

    [Fact]
    public async Task BoundsRead_AnswersTheProvidersBody_ForTheMeasuredViewports()
    {
        var layout = new DatasetLayout(DatasetGenerator.DefaultSeed);
        var viewports = ViewportPlan.For(layout, DatasetGenerator.DefaultSeed, 16, 4, 1580, 1020)
            .Concat(ViewportPlan.For(layout, DatasetGenerator.DefaultSeed, 12, 2, 1580, 1020))
            .Concat(ViewportPlan.For(layout, DatasetGenerator.DefaultSeed, 8, 1, 1580, 1020))
            .ToList();
        var pipeline = new BoundsEndpointPipeline(DataSource, TimeProvider.System);
        var nonEmpty = 0;

        foreach (var viewport in viewports)
        {
            var bench = await pipeline.GetAsync(viewport.Bounds, viewport.Zoom, CancellationToken.None);
            var provider = await ProviderBodyAsync(viewport.Bounds, viewport.Zoom);

            bench.ReturnedFeatures.Should().Be(provider.Count);
            bench.MatchedRows.Should().BeGreaterThanOrEqualTo(bench.ReturnedFeatures);
            bench.Payload.Should().Equal(
                JsonSerializer.SerializeToUtf8Bytes<IReadOnlyList<GeoFeature>>(provider, GeoJsonSerializer.CompactOptions),
                "the same features in the same order, serialized the same way, at z{0}", viewport.Zoom);
            Features(bench.Payload).Select(f => DatasetComposition.Find(f.AssetTypeId)!)
                .Should().OnlyContain(t => t.IsDrawnAt(viewport.Zoom), "the zoom's hidden types never reach the body");

            if (provider.Count > 0) nonEmpty++;
        }

        nonEmpty.Should().BeGreaterThan(viewports.Count / 2, "the viewports are drawn where the assets are");
    }

    [Fact]
    public async Task BoundsRead_ReadsOnlyTheFirstMatchesByOrdinalId_UpToTheEndpointsLimit()
    {
        var extent = DatasetLayout.Extent;
        var everything = new GeoBounds(extent.MinLon - 1, extent.MinLat - 1, extent.MaxLon + 1, extent.MaxLat + 1);
        var drawnAt16 = DatasetComposition.Counts(PostgresBenchDatabase.FeatureCount).Where(c => c.Type.IsDrawnAt(16)).Sum(c => c.Count);

        var bench = await new BoundsEndpointPipeline(DataSource, TimeProvider.System).GetAsync(everything, 16, CancellationToken.None);
        var provider = await ProviderBodyAsync(everything, 16);

        drawnAt16.Should().BeGreaterThan(BoundsEndpointPipeline.MaxBoundsFeatureCount, "the case needs more matches than the cap");
        bench.MatchedRows.Should().Be(drawnAt16);
        bench.ReturnedFeatures.Should().Be(BoundsEndpointPipeline.MaxBoundsFeatureCount);
        provider.Should().HaveCount(BoundsEndpointPipeline.MaxBoundsFeatureCount, "the provider's LIMIT is the endpoint's cap");
        var ids = Features(bench.Payload).ConvertAll(f => f.Id);
        ids.Should().Equal(provider.Select(f => f.Id)).And.BeInAscendingOrder(StringComparer.Ordinal);
        bench.Database.Should().BePositive();
        bench.Total.Should().BeGreaterThanOrEqualTo(bench.Database);
    }

    [Fact]
    public async Task BoundsRead_BrotliBody_DecodesToTheBody()
    {
        var viewport = ViewportPlan.For(new DatasetLayout(DatasetGenerator.DefaultSeed), DatasetGenerator.DefaultSeed, 12, 1, 1580, 1020)[0];

        var bench = await new BoundsEndpointPipeline(DataSource, TimeProvider.System).GetAsync(viewport.Bounds, viewport.Zoom, CancellationToken.None);

        await using var decoder = new BrotliStream(new MemoryStream(bench.BrotliPayload), CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        await decoder.CopyToAsync(decoded);
        decoded.ToArray().Should().Equal(bench.Payload);
        bench.BrotliPayload.Length.Should().BeLessThan(bench.Payload.Length);
    }

    [Fact]
    public async Task Benchmark_RunsEachZoom_AndReportsTheMarkdownTables()
    {
        var settings = new BenchSettings(DatasetGenerator.DefaultSeed, [12, 16], ViewportsPerZoom: 2, Iterations: 2, WarmupIterations: 1, 1580, 1020);
        var time = TimeProvider.System;
        var benchmark = new RenderBenchmark(new BoundsEndpointPipeline(DataSource, time), new SimulatedClientRenderer(time), TextWriter.Null);

        var zooms = await benchmark.RunAsync(settings, new DatasetLayout(settings.Seed), CancellationToken.None);
        var environment = await BenchEnvironment.CaptureAsync(DataSource, time, CancellationToken.None);
        var markdown = new BenchReport(environment, settings, zooms).ToMarkdown();

        zooms.Select(z => z.Zoom).Should().Equal(12, 16);
        zooms.Should().OnlyContain(z => z.Viewports.Count == 2 && z.ServerMs.Count == 4 && z.ClientMs.Count == 4 && z.PayloadBytes.Count == 2);
        zooms.SelectMany(z => z.Viewports).Should().OnlyContain(v => v.PayloadBytes >= 2 && v.DrawnFeatures <= v.ReturnedFeatures);
        environment.DatasetFeatures.Should().Be(PostgresBenchDatabase.FeatureCount);
        environment.PostGisVersion.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("| z12 |").And.Contain("| z16 |").And.Contain("#### z16 — per viewport");
    }

    /// <summary>What the endpoint answers: <c>GetInViewportAsync</c> with the viewport's zoom and the endpoint's cap.</summary>
    private async Task<IReadOnlyList<GeoFeature>> ProviderBodyAsync(GeoBounds bounds, int zoom)
    {
        var options = PostgresBenchDatabase.CreateOptions(_connectionString);
        await using var db = new GeoAssetsDbContext(options);
        await using var provider = new PostgresAssetProvider(db, options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        return await provider.GetInViewportAsync(
            new ViewportQuery(new BoundingBox(bounds.MinLon, bounds.MinLat, bounds.MaxLon, bounds.MaxLat), zoom),
            BoundsEndpointPipeline.MaxBoundsFeatureCount);
    }

    /// <summary>The body's features' ids and asset types, in body order.</summary>
    private static List<(string Id, string AssetTypeId)> Features(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        return
        [
            .. document.RootElement.EnumerateArray().Select(f =>
                (f.GetProperty("id").GetString()!, f.GetProperty("properties").GetProperty("assetTypeId").GetString()!)),
        ];
    }

    private Task<string> TableDigestAsync() => ScalarAsync<string>(
        """
        SELECT md5(string_agg(concat_ws('|', "Id", "Name", "AssetTypeId", "Description", "CreatedAt", "UpdatedAt",
                                        encode(ST_AsEWKB(geom), 'hex'), custom_attributes::text), ',' ORDER BY "Id"))
        FROM geo_entity
        """);

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
