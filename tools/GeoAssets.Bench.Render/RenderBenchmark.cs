using System.IO.Compression;

namespace GeoAssets.Bench.Render;

/// <summary>How a benchmark run samples: the dataset seed, viewports per zoom, measured and warm-up passes, viewport size.</summary>
internal sealed record BenchSettings(
    ulong Seed,
    IReadOnlyList<int> Zooms,
    int ViewportsPerZoom,
    int Iterations,
    int WarmupIterations,
    int WidthPx,
    int HeightPx);

/// <summary>One viewport's response (deterministic for the dataset) and its median timings.</summary>
internal sealed record ViewportResult(
    double CenterLon,
    double CenterLat,
    GeoBounds Bounds,
    long PayloadBytes,
    long BrotliBytes,
    long GzipBytes,
    int MatchedRows,
    int ReturnedFeatures,
    int DrawnFeatures,
    long PathVertices,
    double ServerMsP50,
    double ClientMsP50);

/// <summary>
/// One zoom's results. Size/count percentiles are over the zoom's viewports (one value each — the response is the same
/// on every pass); timing percentiles are over every measured pass of every viewport.
/// </summary>
internal sealed record ZoomResult(
    int Zoom,
    PercentileSummary PayloadBytes,
    PercentileSummary BrotliBytes,
    PercentileSummary GzipBytes,
    PercentileSummary MatchedRows,
    PercentileSummary ReturnedFeatures,
    PercentileSummary DrawnFeatures,
    PercentileSummary ServerMs,
    PercentileSummary DatabaseMs,
    PercentileSummary ClientMs,
    IReadOnlyList<ViewportResult> Viewports);

/// <summary>
/// Runs the XD01-420 scenarios: for each zoom, <see cref="BenchSettings.WarmupIterations"/> unrecorded passes, then
/// <see cref="BenchSettings.Iterations"/> recorded passes over the zoom's viewports — each pass the server pipeline
/// (<see cref="BoundsEndpointPipeline"/>) followed by the simulated client render of its body. Sequential: one request
/// at a time, so the numbers are per-request costs, not contention.
/// </summary>
internal sealed class RenderBenchmark(BoundsEndpointPipeline server, SimulatedClientRenderer client, TextWriter log)
{
    public async Task<IReadOnlyList<ZoomResult>> RunAsync(BenchSettings settings, DatasetLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);

        var results = new List<ZoomResult>(settings.Zooms.Count);
        foreach (var zoom in settings.Zooms)
        {
            var viewports = ViewportPlan.For(layout, settings.Seed, zoom, settings.ViewportsPerZoom, settings.WidthPx, settings.HeightPx);
            results.Add(await RunZoomAsync(zoom, viewports, settings, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<ZoomResult> RunZoomAsync(
        int zoom, IReadOnlyList<MapViewport> viewports, BenchSettings settings, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < settings.WarmupIterations; pass++)
        {
            await log.WriteLineAsync($"z{zoom}: warm-up {pass + 1}/{settings.WarmupIterations}").ConfigureAwait(false);
            foreach (var viewport in viewports)
            {
                var warm = await server.GetAsync(viewport.Bounds, viewport.Zoom, cancellationToken).ConfigureAwait(false);
                client.Render(warm.Payload, viewport);
            }
        }

        var serverMs = viewports.Select(_ => new List<double>()).ToArray();
        var databaseMs = new List<double>();
        var clientMs = viewports.Select(_ => new List<double>()).ToArray();
        var first = new (BoundsResponse Response, ClientRenderResult Render)[viewports.Count];

        for (var pass = 0; pass < settings.Iterations; pass++)
        {
            await log.WriteLineAsync($"z{zoom}: pass {pass + 1}/{settings.Iterations}").ConfigureAwait(false);
            for (var v = 0; v < viewports.Count; v++)
            {
                var response = await server.GetAsync(viewports[v].Bounds, viewports[v].Zoom, cancellationToken).ConfigureAwait(false);
                var render = client.Render(response.Payload, viewports[v]);
                serverMs[v].Add(response.Total.TotalMilliseconds);
                databaseMs.Add(response.Database.TotalMilliseconds);
                clientMs[v].Add(render.Elapsed.TotalMilliseconds);
                if (pass == 0) first[v] = (response, render);
            }
        }

        var perViewport = viewports.Select((viewport, v) => new ViewportResult(
            viewport.CenterLon,
            viewport.CenterLat,
            viewport.Bounds,
            first[v].Response.Payload.LongLength,
            first[v].Response.BrotliPayload.LongLength,
            GzipLength(first[v].Response.Payload),
            first[v].Response.MatchedRows,
            first[v].Response.ReturnedFeatures,
            first[v].Render.DrawnFeatures,
            first[v].Render.PathVertices,
            PercentileSummary.Of(serverMs[v]).P50,
            PercentileSummary.Of(clientMs[v]).P50)).ToList();

        return new ZoomResult(
            zoom,
            PercentileSummary.Of(perViewport.Select(r => (double)r.PayloadBytes)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.BrotliBytes)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.GzipBytes)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.MatchedRows)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.ReturnedFeatures)),
            PercentileSummary.Of(perViewport.Select(r => (double)r.DrawnFeatures)),
            PercentileSummary.Of(serverMs.SelectMany(s => s)),
            PercentileSummary.Of(databaseMs),
            PercentileSummary.Of(clientMs.SelectMany(s => s)),
            perViewport);
    }

    /// <summary>The body's size gzip-compressed at <c>Fastest</c> — what the kernel sends a caller that accepts gzip but not Brotli (XD01-430).</summary>
    internal static long GzipLength(byte[] payload)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(payload);
        return buffer.Length;
    }
}
