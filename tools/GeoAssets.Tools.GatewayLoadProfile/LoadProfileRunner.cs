using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace GeoAssets.Tools.GatewayLoadProfile;

/// <summary>
/// Replays <see cref="LoadProfileModel"/>'s traffic against a gateway (XD01-393) and reports requests/second per class:
/// a map session (pans/zooms issuing WMS tiles + <c>features/bounds</c> API calls, paced at the model's actions/minute) and
/// a mobile photo-sync burst (the model's upload requests, as fast as <c>concurrency</c> allows).
/// </summary>
internal sealed class LoadProfileRunner(HttpClient http, LoadProfileModel model, LoadProfileOptions options, TimeProvider timeProvider, TextWriter output)
{
    private const string TileClass = "tiles";
    private const string ApiClass = "map-api";
    private const string UploadClass = "upload";

    private readonly ConcurrentDictionary<string, ClassStats> _stats = new(StringComparer.Ordinal);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var scenarios = new List<Task>();
        if (options.Scenario is "map" or "all") scenarios.Add(RunMapSessionAsync(cancellationToken));
        if (options.Scenario is "upload" or "all") scenarios.Add(RunUploadBurstAsync(cancellationToken));

        try
        {
            await Task.WhenAll(scenarios);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("Cancelled — reporting what was sent.");
        }

        Report(timeProvider.GetElapsedTime(started));
    }

    private async Task RunMapSessionAsync(CancellationToken cancellationToken)
    {
        var actionInterval = TimeSpan.FromSeconds(60 / model.ActionsPerMinute);
        var end = timeProvider.GetTimestamp() + (long)(options.Duration.TotalSeconds * timeProvider.TimestampFrequency);
        var zoom = 0;
        for (var action = 0; timeProvider.GetTimestamp() < end; action++)
        {
            var actionStarted = timeProvider.GetTimestamp();

            // Deterministic mix: every 1/ZoomShare-th action is a zoom (whole viewport), the others pans (PanReveal of it).
            var isZoom = (action + 1) * model.ZoomShare >= zoom + 1;
            if (isZoom) zoom++;
            var tilesPerLayer = (int)Math.Ceiling(model.TilesPerRepaint * (isZoom ? 1 : model.PanReveal));

            var requests = new List<Func<Task>>();
            for (var layer = 0; layer < model.WmsLayers; layer++)
            {
                for (var tile = 0; tile < tilesPerLayer; tile++)
                {
                    var uri = TileUri(action, layer, tile);
                    requests.Add(() => SendAsync(TileClass, () => new HttpRequestMessage(HttpMethod.Get, uri), anonymous: true, cancellationToken));
                }
            }

            for (var entry = 0; entry < model.VectorEntries; entry++)
            {
                var uri = BoundsUri(action, entry);
                requests.Add(() => SendAsync(ApiClass, () => new HttpRequestMessage(HttpMethod.Get, uri), anonymous: false, cancellationToken));
            }

            await RunWithConcurrencyAsync(requests, options.Concurrency, cancellationToken);

            var remaining = actionInterval - timeProvider.GetElapsedTime(actionStarted);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, timeProvider, cancellationToken);
        }
    }

    private Task RunUploadBurstAsync(CancellationToken cancellationToken)
    {
        var payload = new byte[options.UploadBytes];
        var requests = new List<Func<Task>>();
        for (var photo = 0; photo < model.Photos; photo++)
        {
            for (var part = 0; part < model.RequestsPerPhoto; part++)
            {
                requests.Add(() => SendAsync(UploadClass, () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, options.UploadPath) { Content = new ByteArrayContent(payload) };
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    return request;
                }, anonymous: false, cancellationToken));
            }
        }

        for (var extra = 0; extra < model.UploadExtraRequests; extra++)
            requests.Add(() => SendAsync(UploadClass, () => new HttpRequestMessage(HttpMethod.Get, options.UploadExtraPath), anonymous: false, cancellationToken));

        return RunWithConcurrencyAsync(requests, options.UploadConcurrency, cancellationToken);
    }

    private async Task SendAsync(string requestClass, Func<HttpRequestMessage> createRequest, bool anonymous, CancellationToken cancellationToken)
    {
        var stats = _stats.GetOrAdd(requestClass, _ => new ClassStats());
        using var request = createRequest();
        if (!anonymous && options.BearerToken is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stats.Record(response.StatusCode);
        }
        catch (HttpRequestException)
        {
            stats.RecordTransportError();
        }
    }

    private static Task RunWithConcurrencyAsync(List<Func<Task>> requests, int concurrency, CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(
            requests,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
            async (send, _) => await send());

    private static Uri TileUri(int action, int layer, int tile)
    {
        // A distinct 4326 tile bbox per request, so no cache answers it before the gateway.
        var size = 0.01;
        var minLon = -47.9 + (tile * size);
        var minLat = -15.8 + (action * size);
        return new Uri(string.Create(CultureInfo.InvariantCulture,
            $"/wms?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=geoassets:feature&STYLES=&FORMAT=image/png&TRANSPARENT=true&SRS=EPSG:4326&WIDTH=256&HEIGHT=256&BBOX={minLon},{minLat + (layer * size)},{minLon + size},{minLat + size}"),
            UriKind.Relative);
    }

    private static Uri BoundsUri(int action, int entry)
    {
        var minLon = -47.9 + (action * 0.05);
        var minLat = -15.8 + (entry * 0.05);
        return new Uri(string.Create(CultureInfo.InvariantCulture,
            $"/api/geoassets/features/bounds?minLon={minLon}&minLat={minLat}&maxLon={minLon + 0.1}&maxLat={minLat + 0.06}"),
            UriKind.Relative);
    }

    private void Report(TimeSpan elapsed)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Elapsed {elapsed.TotalSeconds:0.0} s against {http.BaseAddress}");
        text.AppendLine("class      requests   req/s   2xx    429    other  transport-errors");
        foreach (var (requestClass, stats) in _stats.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var rate = elapsed.TotalSeconds > 0 ? stats.Total / elapsed.TotalSeconds : 0;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{requestClass,-10} {stats.Total,8} {rate,7:0.0} {stats.Success,5} {stats.RateLimited,6} {stats.Other,8} {stats.TransportErrors,17}");
        }

        output.Write(text.ToString());
    }

    private sealed class ClassStats
    {
        private int _success;
        private int _rateLimited;
        private int _other;
        private int _transportErrors;

        public int Success => _success;

        public int RateLimited => _rateLimited;

        public int Other => _other;

        public int TransportErrors => _transportErrors;

        public int Total => _success + _rateLimited + _other + _transportErrors;

        public void Record(HttpStatusCode status)
        {
            if ((int)status is >= 200 and < 300) Interlocked.Increment(ref _success);
            else if (status == HttpStatusCode.TooManyRequests) Interlocked.Increment(ref _rateLimited);
            else Interlocked.Increment(ref _other);
        }

        public void RecordTransportError() => Interlocked.Increment(ref _transportErrors);
    }
}
