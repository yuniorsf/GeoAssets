using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.MapTiles;
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

    /// <summary>A box or fence selection gesture was completed on the map (XD01-310).</summary>
    [Parameter] public EventCallback<MapSelectionGesture> OnSelectionGesture { get; set; }

    /// <summary>A feature was clicked while the click-toggle selection tool was armed (XD01-310).</summary>
    [Parameter] public EventCallback<(string FeatureId, bool Additive)> OnSelectionClick { get; set; }

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

    /// <summary>Cancelled and replaced at the top of every <see cref="RenderViewportAsync"/> call so
    /// a newer pan always supersedes a still-in-flight older one (XD01-170 finding #2) — without
    /// this, rapid pans could leave multiple bounds fetches in flight concurrently, each eventually
    /// rendering its (possibly stale) result on top of whatever the latest pan already drew.</summary>
    private CancellationTokenSource? _viewportRenderCts;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _initialized) return;
        _initialized = true;

        Logger.LogDebug("MapContainer initializing — divId={DivId} lat={Lat} lon={Lon} zoom={Zoom}",
            DivId, InitLat, InitLon, InitZoom);

        _dotNetRef = DotNetObjectReference.Create((object)this);
        await MapInterop.InitializeMapAsync(DivId, InitLat, InitLon, InitZoom);
        await MapInterop.RegisterEventHandlersAsync(DivId, _dotNetRef);

        // A fresh map knows no zoom ranges: start from a clean slate and push them before the
        // first render, so a type outside its range never flashes onto the map (XD01-311).
        LayerDisplay.Forget(DivId);
        await SyncZoomRangesAsync();

        await RenderAllViaPipelineAsync();

        Repository.FeatureAdded      += OnFeatureAdded;
        Repository.FeatureUpdated    += OnFeatureUpdated;
        Repository.FeatureDeleted    += OnFeatureDeletedFromRepo;
        Repository.CollectionChanged += OnCollectionChanged;

        // Another organization's reference layers (XD01-331) switched on earlier in the session are
        // drawn on this map for its current view — last, since it may wait on the server. They never
        // pass through Repository.
        await ReferenceLayers.AttachAsync(DivId);
    }

    // ─── Repository event handlers ────────────────────────────────────────

    private void OnFeatureAdded(object? _, GeoFeature f)   =>
        _ = InvokeAsync(() => MapInterop.RenderFeatureAsync(DivId, f));

    private void OnFeatureUpdated(object? _, GeoFeature f) =>
        _ = InvokeAsync(() => MapInterop.RenderFeatureAsync(DivId, f));

    private void OnFeatureDeletedFromRepo(object? _, string id) =>
        _ = InvokeAsync(() => MapInterop.RemoveFeatureAsync(DivId, id));

    // A collection change is also how a different set of asset types arrives (a saved collection
    // loaded, the active provider switched), so the zoom ranges are brought up to date first.
    private void OnCollectionChanged(object? _, EventArgs __) =>
        _ = InvokeAsync(async () =>
        {
            await SyncZoomRangesAsync();
            await (ShouldRenderLastViewport(_lastViewport)
                ? RenderViewportAsync(_lastViewport!.Value)
                : RenderAllViaPipelineAsync());
        });

    /// <summary>
    /// Pushes the active provider's per-type zoom ranges to the map (XD01-311) — only what
    /// changed since the last push. Types added or deleted in <c>AssetTypeManager</c> raise no
    /// repository event; that component pushes for itself through the same service.
    /// </summary>
    private Task SyncZoomRangesAsync() => LayerDisplay.SyncZoomRangesAsync(DivId, Repository.GetAssetTypes());

    /// <summary>
    /// Decides whether a collection-change re-render should target the last known viewport
    /// (bounds-filtered, reuses the already-correct pan path) or fall back to an unbounded
    /// full-dataset pipeline render. Pure decision, factored out for direct testability — same
    /// reasoning as <see cref="ResolveDrawnAssetTypeId"/> (this repo has no bUnit yet).
    /// </summary>
    public static bool ShouldRenderLastViewport((double MinLon, double MinLat, double MaxLon, double MaxLat)? lastViewport) =>
        lastViewport is not null;

    /// <summary>
    /// Whether a pool entry's features/tiles belong on the map right now — every open entry
    /// renders, regardless of whether it's the <see cref="IProviderPool.Active"/> one; only
    /// <see cref="ProviderEntry.IsEnabled"/> (the "hide" toggle, surfaced as Show/Hide in
    /// <c>ProviderPoolPanel</c>) excludes it. Factored out for direct unit testing, same reasoning
    /// as <see cref="ShouldRenderLastViewport"/>.
    /// </summary>
    public static bool ShouldShowOnMap(ProviderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsOpen && entry.IsEnabled;
    }

    /// <summary>
    /// Resolves the <see cref="IAssetProvider"/> to actually read from for a given pool entry —
    /// <see cref="Repository"/> (the decorated <c>ActiveAssetProvider</c> chain: observability +
    /// validation) for the active entry, so it keeps getting that instrumentation, and the entry's
    /// own raw provider for every other open overlay, matching how <c>ProviderPoolPanel</c> and
    /// <c>ProviderConnectionMapRenderer</c> already read non-active entries directly.
    /// </summary>
    private IAssetProvider ProviderFor(ProviderEntry entry) => entry.IsActive ? Repository : entry.Provider;

    /// <summary>
    /// Full-dataset bulk render (initial load, or a collection change with no known viewport yet).
    /// Walks every pool entry (not just the active one) so every open-and-not-hidden provider —
    /// vector or WMS — is rendered, not only whichever entry happens to be active for editing.
    /// Vector entries stream through <see cref="FeatureRenderPipeline"/> in <see cref="BulkRenderChunkSize"/>-feature
    /// chunks so a large import doesn't block the UI thread with one unbounded render call — see
    /// XD01-159's audit and XD01-160. Cancelled by <see cref="RenderViewportAsync"/> if a pan arrives
    /// mid-render.
    /// </summary>
    private async Task RenderAllViaPipelineAsync()
    {
        if (_bulkRenderCts is { } existing) await existing.CancelAsync();
        var cts = new CancellationTokenSource();
        _bulkRenderCts = cts;
        var ct = cts.Token;

        var sw = Stopwatch.StartNew();
        var count = 0;
        try
        {
            await MapInterop.ClearAllFeaturesAsync(DivId);
            foreach (var entry in Pool.All)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.Provider is IWmsProvider wms)
                {
                    await ReconcileWmsLayerAsync(entry, wms);
                    continue;
                }

                if (!ShouldShowOnMap(entry)) continue;

                await foreach (var chunk in Pipeline.StreamAllAsync(ProviderFor(entry), BulkRenderChunkSize, ct))
                {
                    await MapInterop.RenderFeatureBatchAsync(DivId, chunk);
                    count += chunk.Count;
                }
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

    /// <summary>
    /// Adds or removes a WMS entry's tile layer so it matches <see cref="ShouldShowOnMap"/> —
    /// both JS-side calls are idempotent (see <c>geoassets.js</c>'s <c>addWmsLayer</c>/<c>removeWmsLayer</c>),
    /// and WMS tile layers are never touched by <see cref="IMapInterop.ClearAllFeaturesAsync"/>
    /// (that only clears vector features), so this only needs to run once per bulk render, not on
    /// every pan.
    /// </summary>
    private Task ReconcileWmsLayerAsync(ProviderEntry entry, IWmsProvider wms) =>
        ShouldShowOnMap(entry)
            ? MapInterop.AddWmsLayerAsync(DivId, entry.Id.ToString(), wms.WmsBaseUrl,
                new WmsLayerOptions(Layers: wms.WmsLayerName, Format: wms.WmsFormat))
            : MapInterop.RemoveWmsLayerAsync(DivId, entry.Id.ToString());

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
    public Task OnFeatureClickedFromJs(string featureId) =>
        OnFeatureClicked.InvokeAsync(featureId);

    [JSInvokable("OnFeatureContextMenuFromJs")]
    public Task OnFeatureContextMenuFromJs(string featureId, double x, double y) =>
        OnFeatureContextMenu.InvokeAsync((featureId, x, y));

    /// <summary>
    /// A selection box or fence was drawn (XD01-310). Unlike <see cref="OnFeatureDrawnFromJs"/> this
    /// never creates a feature and never touches <see cref="Repository"/>: the geometry only
    /// describes an area. Malformed input is logged and ignored.
    /// </summary>
    [JSInvokable("OnSelectionGeometryFromJs")]
    public Task OnSelectionGeometryFromJs(string tool, string ringsJson, bool additive)
    {
        if (!MapSelectionGestureParser.TryParse(tool, ringsJson, additive, out var gesture, out var rejection) || gesture is null)
        {
            Logger.LogWarning("OnSelectionGeometryFromJs — ignored: {Rejection}", rejection);
            return Task.CompletedTask;
        }

        Logger.LogDebug("Selection gesture — tool={Tool} additive={Additive}", gesture.Tool, gesture.Additive);
        return OnSelectionGesture.InvokeAsync(gesture);
    }

    /// <summary>A feature was clicked while the click-toggle selection tool was armed (XD01-310).</summary>
    [JSInvokable("OnSelectionClickFromJs")]
    public Task OnSelectionClickFromJs(string featureId, bool additive)
    {
        if (string.IsNullOrWhiteSpace(featureId))
        {
            Logger.LogWarning("OnSelectionClickFromJs — ignored: blank feature id");
            return Task.CompletedTask;
        }

        return OnSelectionClick.InvokeAsync((featureId, additive));
    }

    /// <summary>
    /// How a WMS layer's tiles are requested (XD01-96): <c>geoassets.js</c> asks once per layer, and again shortly before
    /// the token expires or after a tile is refused with 401. See <see cref="MapTileAccessLookup"/>.
    /// </summary>
    [JSInvokable("GetTileAccessFromJs")]
    public Task<MapTileAccess> GetTileAccessFromJs(string tileUrl) =>
        MapTileAccessLookup.GetAsync(TileAccess, tileUrl, Logger);

    /// <summary>
    /// The view settled: re-renders the providers' features for it and, debounced and superseding any load still in
    /// flight, the switched-on reference layers of other organizations (XD01-331).
    /// </summary>
    [JSInvokable("OnViewportChangedFromJs")]
    public Task OnViewportChangedFromJs(double minLon, double minLat, double maxLon, double maxLat)
    {
        _lastViewport = (minLon, minLat, maxLon, maxLat);
        return Task.WhenAll(
            RenderViewportAsync(_lastViewport.Value),
            ReferenceLayers.OnViewportChangedAsync(DivId, minLon, minLat, maxLon, maxLat));
    }

    /// <summary>
    /// Bounds-filtered render for the given viewport — used both by a live pan
    /// (<see cref="OnViewportChangedFromJs"/>) and by <see cref="OnCollectionChanged"/> once a
    /// viewport is known, so a bulk mutation re-renders only what's visible instead of the whole
    /// dataset. Cancels any in-flight full-dataset bulk render first, so a pan always wins.
    /// Walks every open-and-not-hidden vector entry in the pool (see <see cref="ShouldShowOnMap"/>),
    /// not just the active one, clearing once up front and rendering each entry additively so
    /// providers don't erase each other. WMS entries are skipped here — their tile layers aren't
    /// touched by <see cref="IMapInterop.ClearAllFeaturesAsync"/> and don't need re-fetching on pan.
    /// </summary>
    private async Task RenderViewportAsync((double MinLon, double MinLat, double MaxLon, double MaxLat) bounds)
    {
        if (_bulkRenderCts is { } existingBulk) await existingBulk.CancelAsync();

        if (_viewportRenderCts is { } existingViewport) await existingViewport.CancelAsync();
        var cts = new CancellationTokenSource();
        _viewportRenderCts = cts;
        var ct = cts.Token;

        var (minLon, minLat, maxLon, maxLat) = bounds;
        var sw = Stopwatch.StartNew();

        try
        {
            await MapInterop.ClearAllFeaturesAsync(DivId);

            var totalCount = 0;
            foreach (var entry in Pool.All.Where(e => e.Provider is not IWmsProvider && ShouldShowOnMap(e)))
            {
                ct.ThrowIfCancellationRequested();
                totalCount += await RenderEntryViewportAsync(ProviderFor(entry), minLon, minLat, maxLon, maxLat, ct);
            }

            sw.Stop();
            Logger.LogInformation(
                "Viewport changed [{MinLon},{MinLat},{MaxLon},{MaxLat}] — {Count} features in {ElapsedMs:F1} ms",
                minLon, minLat, maxLon, maxLat, totalCount, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug(
                "Viewport changed [{MinLon},{MinLat},{MaxLon},{MaxLat}] — cancelled (superseded by a newer viewport change)",
                minLon, minLat, maxLon, maxLat);
        }
    }

    /// <summary>
    /// Fetches and additively renders one entry's features within <paramref name="minLon"/>..<paramref name="maxLat"/>.
    /// Prefers the raw-JSON chunked path: the provider streams the HTTP response body(s) as-is, JS
    /// parses each chunk natively and renders it immediately (no WASM JSON parsing bottleneck, no
    /// waiting for one giant payload before anything appears). Providers that don't support raw
    /// JSON (e.g. Postgres) yield zero chunks — see IAssetProvider's default
    /// GetInBoundsRawJsonChunksAsync — which is the signal to fall back to the JSON-element path.
    /// A provider that does support it never yields zero for a real (possibly empty) result: even
    /// an empty bbox response is a non-null "[]" chunk. Returns the number of raw chunks rendered,
    /// or the feature count on the fallback path, purely for the aggregate log line above.
    /// </summary>
    private async Task<int> RenderEntryViewportAsync(
        IAssetProvider provider, double minLon, double minLat, double maxLon, double maxLat, CancellationToken ct)
    {
        var chunkCount = 0;
        await foreach (var chunk in provider.GetInBoundsRawJsonChunksAsync(minLon, minLat, maxLon, maxLat, ct))
        {
            await MapInterop.RenderFeatureBatchRawJsonAsync(DivId, chunk);
            chunkCount++;
        }
        if (chunkCount > 0) return chunkCount;

        // GetInBoundsJsonAsync has no CancellationToken overload (not every provider's fetch
        // supports mid-flight abort — e.g. a plain HTTP GET without one plumbed through), so a
        // superseded call here can't be aborted early, but it must not paint its (now stale)
        // result over whatever a newer pan already rendered.
        var features = await provider.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);
        if (ct.IsCancellationRequested) return 0;
        await MapInterop.RenderFeatureBatchAsync(DivId, features);
        return features.Count;
    }

    public Task PanToFeatureAsync(string featureId) =>
        MapInterop.PanToFeatureAsync(DivId, featureId);

    // ─── Dispose ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        _bulkRenderCts?.Cancel();
        _bulkRenderCts?.Dispose();
        _viewportRenderCts?.Cancel();
        _viewportRenderCts?.Dispose();
        ReferenceLayers.Detach(DivId);

        Repository.FeatureAdded      -= OnFeatureAdded;
        Repository.FeatureUpdated    -= OnFeatureUpdated;
        Repository.FeatureDeleted    -= OnFeatureDeletedFromRepo;
        Repository.CollectionChanged -= OnCollectionChanged;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        if (_initialized)
        {
            Logger.LogDebug("MapContainer disposing — divId={DivId}", DivId);
            LayerDisplay.Forget(DivId);
            await MapInterop.DestroyMapAsync(DivId);
        }
        _dotNetRef?.Dispose();
    }
}
