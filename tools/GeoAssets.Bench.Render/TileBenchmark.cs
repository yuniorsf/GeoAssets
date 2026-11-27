using GeoAssets.Core.Rendering;

namespace GeoAssets.Bench.Render;

/// <summary>One viewport's tiles (deterministic for the dataset) and its first pass's numbers.</summary>
/// <param name="Tiles">The tiles covering the viewport.</param>
/// <param name="NonEmptyTiles">Those with features (the rest are <c>204 No Content</c>).</param>
/// <param name="WireBytes">The tile bodies on the wire, summed: gzip-encoded tiles, 0 for an empty one.</param>
/// <param name="MvtBytes">The tiles as drawn, before the endpoint's gzip, summed.</param>
/// <param name="ColdMsTotal">The viewport's tiles drawn one after another (the first measured pass): the serial lower bound of a cold load.</param>
internal sealed record TileViewportResult(
    double CenterLon, double CenterLat, string Range, int Tiles, int NonEmptyTiles, long WireBytes, long MvtBytes, double ColdMsTotal);

/// <summary>
/// One zoom's tile results. Per-viewport sizes are over the zoom's viewports; per-tile times over every tile of every
/// viewport of every measured pass — cold: drawn by the source and encoded (a cache miss); warm: served from the
/// server's <see cref="MemoryTileCache"/> (a hit).
/// </summary>
internal sealed record TileZoomResult(
    int Zoom,
    PercentileSummary TilesPerViewport,
    PercentileSummary NonEmptyTilesPerViewport,
    PercentileSummary WireBytesPerViewport,
    PercentileSummary MvtBytesPerViewport,
    PercentileSummary ColdTileMs,
    PercentileSummary ColdTileDatabaseMs,
    PercentileSummary WarmTileMs,
    IReadOnlyList<TileViewportResult> Viewports);

/// <summary>
/// The XD01-434 tile scenario: for each zoom, the 256-px tiles covering each of the zoom's viewports (the same viewports
/// as <see cref="RenderBenchmark"/>), requested as MapLibre requests them — <see cref="BenchSettings.WarmupIterations"/>
/// unrecorded passes, then <see cref="BenchSettings.Iterations"/> recorded cold passes through
/// <see cref="TileEndpointPipeline"/> (every tile drawn, nothing cached), then as many warm passes over a
/// <see cref="MemoryTileCache"/> holding what the first pass drew. Sequential: one tile at a time.
/// </summary>
internal sealed class TileBenchmark(TileEndpointPipeline server, TimeProvider timeProvider, TextWriter log)
{
    private static readonly TileCacheScope Scope = TileCacheScope.Create(Guid.Empty.ToString("N"), []);

    public async Task<IReadOnlyList<TileZoomResult>> RunAsync(BenchSettings settings, DatasetLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);

        var results = new List<TileZoomResult>(settings.Zooms.Count);
        foreach (var zoom in settings.Zooms)
        {
            var viewports = ViewportPlan.For(layout, settings.Seed, zoom, settings.ViewportsPerZoom, settings.WidthPx, settings.HeightPx);
            results.Add(await RunZoomAsync(zoom, viewports, settings, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<TileZoomResult> RunZoomAsync(
        int zoom, IReadOnlyList<MapViewport> viewports, BenchSettings settings, CancellationToken cancellationToken)
    {
        var tiles = viewports.Select(v => v.CoveringTiles().EnumerateTiles().ToArray()).ToArray();

        for (var pass = 0; pass < settings.WarmupIterations; pass++)
        {
            await log.WriteLineAsync($"tiles z{zoom}: warm-up {pass + 1}/{settings.WarmupIterations}").ConfigureAwait(false);
            foreach (var tile in tiles.SelectMany(t => t))
                await server.GetAsync(tile, cancellationToken).ConfigureAwait(false);
        }

        var coldMs = new List<double>();
        var databaseMs = new List<double>();
        var first = new TileResponse[viewports.Count][];
        for (var pass = 0; pass < settings.Iterations; pass++)
        {
            await log.WriteLineAsync($"tiles z{zoom}: cold pass {pass + 1}/{settings.Iterations}").ConfigureAwait(false);
            for (var v = 0; v < viewports.Count; v++)
            {
                var responses = new TileResponse[tiles[v].Length];
                for (var t = 0; t < tiles[v].Length; t++)
                {
                    responses[t] = await server.GetAsync(tiles[v][t], cancellationToken).ConfigureAwait(false);
                    coldMs.Add(responses[t].Total.TotalMilliseconds);
                    databaseMs.Add(responses[t].Database.TotalMilliseconds);
                }

                if (pass == 0) first[v] = responses;
            }
        }

        var cache = new MemoryTileCache();
        for (var v = 0; v < viewports.Count; v++)
        {
            for (var t = 0; t < tiles[v].Length; t++)
                await cache.SetAsync(Key(tiles[v][t]), Payload(first[v][t]), cancellationToken).ConfigureAwait(false);
        }

        var warmMs = new List<double>();
        for (var pass = 0; pass < settings.Iterations; pass++)
        {
            foreach (var tile in tiles.SelectMany(t => t))
            {
                var started = timeProvider.GetTimestamp();
                _ = await cache.GetAsync(Key(tile), cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException($"Tile {tile} was not cached.");
                warmMs.Add(timeProvider.GetElapsedTime(started).TotalMilliseconds);
            }
        }

        var perViewport = viewports.Select((viewport, v) => new TileViewportResult(
            viewport.CenterLon,
            viewport.CenterLat,
            viewport.CoveringTiles().ToString(),
            tiles[v].Length,
            first[v].Count(r => r.Mvt.Length > 0),
            first[v].Sum(r => r.WireBytes),
            first[v].Sum(r => (long)r.Mvt.Length),
            first[v].Sum(r => r.Total.TotalMilliseconds))).ToList();

        return new TileZoomResult(
            zoom,
            PercentileSummary.Of(perViewport.Select(r => (double)r.Tiles)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.NonEmptyTiles)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.WireBytes)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.MvtBytes)),
            PercentileSummary.Of(coldMs),
            PercentileSummary.Of(databaseMs),
            PercentileSummary.Of(warmMs),
            perViewport);
    }

    private static TileCacheKey Key(TileAddress tile) => new(Scope, "assets", "bench", tile, TileFormat.Mvt);

    private static TilePayload Payload(TileResponse response) =>
        response.Mvt.Length == 0
            ? TilePayload.Empty(TileFormats.MapboxVectorTileContentType)
            : new TilePayload(TileEndpointPipeline.Gzip(response.Mvt), TileFormats.MapboxVectorTileContentType, "gzip");
}
