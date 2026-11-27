using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Server.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace GeoAssets.Bench.Render.Tests;

/// <summary>
/// The raster tier's cost under a calibrated policy (XD01-439) — what <c>GeoAssets.Bench.Render tiles</c> cannot measure,
/// since <see cref="SkiaRasterTileSource"/> lives in GeoAssets.Server, which the tool must not reference (Invariant 1). An
/// opt-in benchmark, not a check: against the seeded benchmark database (<see cref="BenchOptions.ConnectionVariable"/>, else
/// the docker-compose one), with the policy of the <c>calibrate --out</c> file <see cref="PolicyVariable"/> names, it draws
/// the raster tiles of the tile scenario's viewports at z8, z12 and z16 (or the zooms <see cref="ZoomsVariable"/> lists) as <c>GET /tiles/assets/{v}/{z}/{x}/{y}.png</c>
/// does on a cache miss — the layers that are raster at the zoom, bottom first, through <see cref="SkiaRasterTileSource"/>
/// over <see cref="PostgresAssetRenderQuery"/>, one 2 × 2 metatile drawing per metatile the viewport touches (every tile
/// of it waits for that drawing, so a tile's cold time is its metatile's) — after one warm-up pass, three measured passes,
/// and writes the JSON to <see cref="OutVariable"/> (when set) and the table to the test output.
/// </summary>
public sealed class RasterTierBenchmarkTests(ITestOutputHelper output)
{
    public const string PolicyVariable = "GEOASSETS_RENDER_BENCH_RASTER_LOD";

    public const string OutVariable = "GEOASSETS_RENDER_BENCH_RASTER_OUT";

    /// <summary>The zooms measured, comma-separated (default <c>8,12,16</c>, the tile scenario's).</summary>
    public const string ZoomsVariable = "GEOASSETS_RENDER_BENCH_RASTER_ZOOMS";

    private const int Passes = 3;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static int[] Zooms =>
        Environment.GetEnvironmentVariable(ZoomsVariable) is { Length: > 0 } zooms
            ? [.. zooms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(z => int.Parse(z, CultureInfo.InvariantCulture))]
            : [.. ViewportPlan.StandardZooms];

    [RasterBenchFact]
    public async Task RasterTier_OfTheCalibratedPolicy_OnTheTileScenariosViewports()
    {
        var policy = LodCalibrationReport.ReadPolicy(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable(PolicyVariable)!));
        var connectionString = Environment.GetEnvironmentVariable(BenchOptions.ConnectionVariable) ?? BenchOptions.DefaultConnectionString;
        var options = PostgresBenchDatabase.CreateOptions(connectionString);
        await using var db = new GeoAssetsDbContext(options);
        await using var styling = new PostgresAssetProvider(db, options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        var source = new SkiaRasterTileSource(new PostgresAssetRenderQuery(options, tenantContext: null, styling, TimeProvider.System), policy);
        var layout = new DatasetLayout(DatasetGenerator.DefaultSeed);

        var rows = new List<object>();
        var md = new StringBuilder("| Zoom | Raster layers | Metatiles drawn per viewport | PNG bytes per viewport (p50 / p95) | Cold ms per tile = its metatile's (p50 / p95) |\n|---|---|---|---|---|\n");
        foreach (var zoom in Zooms)
        {
            var layers = RasterLayersAt(zoom, policy);
            if (layers.Count == 0)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| z{zoom} | none | – | – | – |");
                continue;
            }

            var viewports = ViewportPlan.For(layout, DatasetGenerator.DefaultSeed, zoom, 12, ViewportPlan.DefaultWidthPx, ViewportPlan.DefaultHeightPx);
            var tileMs = new List<double>();
            var bytes = new List<double>();
            var metatiles = new List<double>();
            for (var pass = 0; pass <= Passes; pass++)
            {
                foreach (var viewport in viewports)
                {
                    var tiles = viewport.CoveringTiles().EnumerateTiles().ToList();
                    var drawn = new Dictionary<TileAddress, (IReadOnlyDictionary<TileAddress, TilePayload> Tiles, double Ms)>();
                    foreach (var block in tiles.GroupBy(t => t.Metatile(source.MetatileSpan)))
                    {
                        var started = TimeProvider.System.GetTimestamp();
                        var payloads = await source.GetMetatileAsync(new TileRequest(new TileLayerSet("assets", layers), block.First()), CancellationToken.None);
                        var ms = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
                        foreach (var tile in block) drawn[tile] = (payloads, ms);
                    }

                    if (pass == 0) continue;
                    tileMs.AddRange(tiles.Select(t => drawn[t].Ms));
                    metatiles.Add(tiles.Select(t => t.Metatile(source.MetatileSpan)).Distinct().Count());
                    if (pass == 1) bytes.Add(tiles.Sum(t => (double)drawn[t].Tiles[t].Content.Length));
                }
            }

            var ms50 = PercentileSummary.Of(tileMs);
            var b = PercentileSummary.Of(bytes);
            rows.Add(new { Zoom = zoom, Layers = layers, TileMs = ms50, PngBytesPerViewport = b, MetatilesPerViewport = PercentileSummary.Of(metatiles) });
            md.AppendLine(CultureInfo.InvariantCulture, $"| z{zoom} | {string.Join(", ", layers.Select(Name))} | {PercentileSummary.Of(metatiles).P50:N0} | {b.P50 / 1000:F1} kB / {b.P95 / 1000:F1} kB | {ms50.P50:F1} / {ms50.P95:F1} |");
        }

        output.WriteLine(md.ToString());
        if (Environment.GetEnvironmentVariable(OutVariable) is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Policy = policy.Layers, Passes, Zooms = rows }, Json));

        md.ToString().Split('\n').Count(line => line.StartsWith("| z", StringComparison.Ordinal)).Should().Be(Zooms.Length, "one row per zoom, \"none\" where no layer is raster");
    }

    /// <summary>The types a <c>.png</c> tile at <paramref name="zoom"/> carries: drawn there, raster tier, in the endpoint's order (the bench's types have no geometry restriction: name order, as <see cref="TileEndpointPipeline.LayersAt"/>).</summary>
    private static List<string> RasterLayersAt(int zoom, LodPolicy policy) =>
        [.. DatasetComposition.AssetTypes
            .Where(t => t.IsDrawnAt(zoom) && policy.Decide(t.Id.ToString("D"), zoom) == TileRenderTier.Raster)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Id)
            .Select(t => t.Id.ToString("D"))];

    private static string Name(string assetTypeId) => DatasetComposition.Find(assetTypeId)!.Name;
}
