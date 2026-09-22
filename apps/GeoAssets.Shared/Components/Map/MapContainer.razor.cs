using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Diagnostics;

namespace GeoAssets.Shared.Components.Map;

public partial class MapContainer
{
    [Parameter] public string DivId { get; set; } = "geoassets-map";
    [Parameter] public double InitLat { get; set; } = 20.0;
    [Parameter] public double InitLon { get; set; } = -77.0;
    [Parameter] public int InitZoom { get; set; } = 5;

    [Parameter] public EventCallback<GeoFeature> OnFeatureDrawn { get; set; }
    [Parameter] public EventCallback<GeoFeature> OnFeatureEdited { get; set; }
    [Parameter] public EventCallback<string> OnFeatureClicked { get; set; }
    [Parameter] public EventCallback<(string FeatureId, double X, double Y)> OnFeatureContextMenu { get; set; }

    /// <summary>Feature count per <see cref="FeatureRenderPipeline.StreamAllAsync"/> chunk for a
    /// full-dataset bulk render — coarser than <c>MapInteropService</c>'s own <c>BatchSize</c>
    /// JS-batch chunking, which further subdivides each pipeline chunk.</summary>
    private const int BulkRenderChunkSize = 1500;

    // Use DotNetObjectReference<object> to avoid generic covariance issue
    private DotNetObjectReference<object>? _dotNetRef;
    private bool _initialized;

    /// <summary>The most recent viewport bounds reported by <see cref="OnViewportChangedFromJs"/>,
    /// if any — lets <see cref="OnCollectionChanged"/> prefer a bounds-filtered re-render over an
    /// unbounded full-dataset one once the map has panned at least once.</summary>
    private (double MinLon, double MinLat, double MaxLon, double MaxLat)? _lastViewport;

    /// <summary>Cancelled at the top of <see cref="RenderViewportAsync"/> so a pan always wins over
    /// an in-flight full-dataset bulk render (<see cref="RenderAllViaPipelineAsync"/>).</summary>
    private CancellationTokenSource? _bulkRenderCts;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _initialized) return;
        _initialized = true;

        Logger.LogDebug("MapContainer initializing — divId={DivId} lat={Lat} lon={Lon} zoom={Zoom}",
            DivId, InitLat, InitLon, InitZoom);

        _dotNetRef = DotNetObjectReference.Create((object)this);
        await MapInterop.InitializeMapAsync(DivId, InitLat, InitLon, InitZoom);
        await MapInterop.RegisterEventHandlersAsync(DivId, _dotNetRef);

        await RenderAllViaPipelineAsync();

        Repository.FeatureAdded      += OnFeatureAdded;
        Repository.FeatureUpdated    += OnFeatureUpdated;
        Repository.FeatureDeleted    += OnFeatureDeletedFromRepo;
        Repository.CollectionChanged += OnCollectionChanged;
    }

    // ─── Repository event handlers ────────────────────────────────────────

    private void OnFeatureAdded(object? _, GeoFeature f)   =>
        InvokeAsync(() => MapInterop.RenderFeatureAsync(DivId, f));

    private void OnFeatureUpdated(object? _, GeoFeature f) =>
        InvokeAsync(() => MapInterop.RenderFeatureAsync(DivId, f));

    private void OnFeatureDeletedFromRepo(object? _, string id) =>
        InvokeAsync(() => MapInterop.RemoveFeatureAsync(DivId, id));

    private void OnCollectionChanged(object? _, EventArgs __) =>
        InvokeAsync(() => ShouldRenderLastViewport(_lastViewport)
            ? RenderViewportAsync(_lastViewport!.Value)
            : RenderAllViaPipelineAsync());

    /// <summary>
    /// Decides whether a collection-change re-render should target the last known viewport
    /// (bounds-filtered, reuses the already-correct pan path) or fall back to an unbounded
    /// full-dataset pipeline render. Pure decision, factored out for direct testability — same
    /// reasoning as <see cref="ResolveDrawnAssetTypeId"/> (this repo has no bUnit yet).
    /// </summary>
    public static bool ShouldRenderLastViewport((double MinLon, double MinLat, double MaxLon, double MaxLat)? lastViewport) =>
        lastViewport is not null;

    /// <summary>
    /// Full-dataset bulk render (initial load, or a collection change with no known viewport yet)
    /// streamed through <see cref="FeatureRenderPipeline"/> in <see cref="BulkRenderChunkSize"/>-feature
    /// chunks so a large import doesn't block the UI thread with one unbounded render call — see
    /// XD01-159's audit and XD01-160. Cancelled by <see cref="RenderViewportAsync"/> if a pan arrives
    /// mid-render.
    /// </summary>
    private async Task RenderAllViaPipelineAsync()
    {
        _bulkRenderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _bulkRenderCts = cts;

        var sw = Stopwatch.StartNew();
        var count = 0;
        try
        {
            await MapInterop.ClearAllFeaturesAsync(DivId);
            await foreach (var chunk in Pipeline.StreamAllAsync(Repository, BulkRenderChunkSize, cts.Token))
            {
                await MapInterop.RenderFeatureBatchAsync(DivId, chunk);
                count += chunk.Count;
            }
            sw.Stop();
            Logger.LogInformation(
                "Bulk render via pipeline — {Count} features in {ElapsedMs:F1} ms",
                count, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug(
                "Bulk render via pipeline — cancelled after {Count} features (superseded by a pan or a newer bulk render)",
                count);
        }
    }

    // ─── JS → .NET callbacks ─────────────────────────────────────────────

    [JSInvokable("OnFeatureDrawnFromJs")]
    public async Task OnFeatureDrawnFromJs(string geoJson)
    {
        var feature = GeoJsonSerializer.DeserializeFeature(geoJson);
        if (feature is null)
        {
            Logger.LogWarning("OnFeatureDrawnFromJs — failed to deserialize GeoJSON");
            return;
        }

        var pendingTypeId = PendingType.AssetTypeId;
        if (pendingTypeId is null)
        {
            // No type-first selection was pending (the user drew via one of DrawToolbar's 3 raw
            // geometry buttons) — fall back to the original geometry-based inference so those
            // buttons keep working exactly as before XD01-117.
            Logger.LogDebug("OnFeatureDrawnFromJs — no pending AssetType, falling back to geometry-based inference");
        }
        feature.Properties.AssetTypeId = ResolveDrawnAssetTypeId(feature.Geometry, pendingTypeId);
        PendingType.Clear();

        Logger.LogDebug("Feature drawn — id={Id} type={Type}", feature.Id, feature.Geometry?.GetType().Name);
        await OnFeatureDrawn.InvokeAsync(feature);
    }

    /// <summary>
    /// Resolves the <c>AssetTypeId</c> for a newly drawn feature: <paramref name="pendingAssetTypeId"/>
    /// (set by <c>DrawToolbar</c>'s type palette, XD01-117) when present, otherwise the original
    /// geometry-based inference — the 3 built-in generic types, for draws made via DrawToolbar's 3
    /// raw geometry buttons. Static so it's directly unit-testable without rendering.
    /// </summary>
    public static string ResolveDrawnAssetTypeId(GeoGeometry? geometry, string? pendingAssetTypeId) =>
        pendingAssetTypeId ?? geometry switch
        {
            GeoLineString => AssetType.Line.Id.ToString(),
            GeoPolygon    => AssetType.Area.Id.ToString(),
            _             => AssetType.Point.Id.ToString()
        };

    [JSInvokable("OnFeatureEditedFromJs")]
    public async Task OnFeatureEditedFromJs(string featureId, string geometryJson)
    {
        var feature = Repository.GetById(featureId);
        if (feature is null)
        {
            Logger.LogWarning("OnFeatureEditedFromJs — feature not found id={Id}", featureId);
            return;
        }

        var newGeometry = GeoJsonSerializer.DeserializeGeometry(geometryJson);
        if (newGeometry is not null)
        {
            feature.Geometry = newGeometry;
            Repository.Update(feature);
            Logger.LogDebug("Feature edited — id={Id} type={Type}", feature.Id, newGeometry.GetType().Name);
            await OnFeatureEdited.InvokeAsync(feature);
        }
    }

    [JSInvokable("OnFeatureClickedFromJs")]
    public async Task OnFeatureClickedFromJs(string featureId) =>
        await OnFeatureClicked.InvokeAsync(featureId);

    [JSInvokable("OnFeatureContextMenuFromJs")]
    public async Task OnFeatureContextMenuFromJs(string featureId, double x, double y) =>
        await OnFeatureContextMenu.InvokeAsync((featureId, x, y));

    [JSInvokable("OnViewportChangedFromJs")]
    public async Task OnViewportChangedFromJs(double minLon, double minLat, double maxLon, double maxLat)
    {
        _lastViewport = (minLon, minLat, maxLon, maxLat);
        await RenderViewportAsync(_lastViewport.Value);
    }

    /// <summary>
    /// Bounds-filtered render for the given viewport — used both by a live pan
    /// (<see cref="OnViewportChangedFromJs"/>) and by <see cref="OnCollectionChanged"/> once a
    /// viewport is known, so a bulk mutation re-renders only what's visible instead of the whole
    /// dataset. Cancels any in-flight full-dataset bulk render first, so a pan always wins.
    /// </summary>
    private async Task RenderViewportAsync((double MinLon, double MinLat, double MaxLon, double MaxLat) bounds)
    {
        _bulkRenderCts?.Cancel();

        var (minLon, minLat, maxLon, maxLat) = bounds;
        var sw = Stopwatch.StartNew();

        // Prefer the raw-JSON chunked path: the provider streams the HTTP response body(s) as-is,
        // JS parses each chunk natively and renders it immediately (no WASM JSON parsing
        // bottleneck, no waiting for one giant payload before anything appears). Providers that
        // don't support raw JSON (e.g. Postgres) yield zero chunks — see IAssetProvider's default
        // GetInBoundsRawJsonChunksAsync — which is the signal to fall back below. A provider that
        // does support it never yields zero for a real (possibly empty) result: even an empty bbox
        // response is a non-null "[]" chunk.
        await MapInterop.ClearAllFeaturesAsync(DivId);
        var chunkCount = 0;
        await foreach (var chunk in Repository.GetInBoundsRawJsonChunksAsync(minLon, minLat, maxLon, maxLat))
        {
            await MapInterop.RenderFeatureBatchRawJsonAsync(DivId, chunk);
            chunkCount++;
        }

        if (chunkCount > 0)
        {
            sw.Stop();
            Logger.LogInformation(
                "Viewport changed [{MinLon},{MinLat},{MaxLon},{MaxLat}] — raw path, {ChunkCount} chunk(s) in {ElapsedMs:F1} ms",
                minLon, minLat, maxLon, maxLat, chunkCount, sw.Elapsed.TotalMilliseconds);
            return;
        }

        sw.Restart();
        var features = await Repository.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);
        var fetchMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        await MapInterop.RenderAllFeaturesAsync(DivId, features);
        sw.Stop();
        Logger.LogInformation(
            "Viewport changed [{MinLon},{MinLat},{MaxLon},{MaxLat}] — {Count} features, fetch={FetchMs:F1} ms render={RenderMs:F1} ms",
            minLon, minLat, maxLon, maxLat, features.Count, fetchMs, sw.Elapsed.TotalMilliseconds);
    }

    public Task PanToFeatureAsync(string featureId) =>
        MapInterop.PanToFeatureAsync(DivId, featureId);

    // ─── Dispose ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        _bulkRenderCts?.Cancel();

        Repository.FeatureAdded      -= OnFeatureAdded;
        Repository.FeatureUpdated    -= OnFeatureUpdated;
        Repository.FeatureDeleted    -= OnFeatureDeletedFromRepo;
        Repository.CollectionChanged -= OnCollectionChanged;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_initialized)
        {
            Logger.LogDebug("MapContainer disposing — divId={DivId}", DivId);
            await MapInterop.DestroyMapAsync(DivId);
        }
        _dotNetRef?.Dispose();
    }
}
