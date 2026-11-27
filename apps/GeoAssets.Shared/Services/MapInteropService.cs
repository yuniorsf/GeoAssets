using GeoAssets.Shared.Interfaces;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Shared.Services.MapTiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Implements IMapInterop and IMapSelectionInterop by delegating every call to window.GeoAssets.*
/// functions defined in geoassets.js — or, in <see cref="MapRenderMode.MapLibre"/> (XD01-435), to the same functions of
/// window.GeoAssetsMapLibre, defined by geoassets.maplibre.js, which this service imports before its first call so a
/// Leaflet map never loads it. In that mode it also keeps the map's tiles current: every
/// <see cref="IAssetProvider.FeatureAdded"/>, <see cref="IAssetProvider.FeatureUpdated"/>,
/// <see cref="IAssetProvider.FeatureDeleted"/> and <see cref="IAssetProvider.CollectionChanged"/> schedules a re-read
/// of the tile style (<see cref="MapTileStyleRefresher"/>), as does its periodic timer. The Leaflet modes do the same for a
/// map once its kernel tile layer is shown (<see cref="ShowTileLayersAsync"/>, the interim Leaflet split of XD01-436), so a
/// Leaflet map without one re-reads nothing. A local raster tile layer (<see cref="IMapLocalTileLayerInterop"/>, XD01-465)
/// hands JS a <see cref="DotNetObjectReference{TValue}"/> of a <see cref="LocalRasterTileReader"/> per layer, kept until
/// the layer is removed or this service is disposed.
/// </summary>
public sealed class MapInteropService : IMapInterop, IMapSelectionInterop, IMapTileLayerInterop, IMapLocalTileLayerInterop, IAsyncDisposable
{
    /// <summary>Maximum feature ids sent to <c>GeoAssets.setFeatureStates</c> in one JS call (XD01-309).</summary>
    public const int FeatureStateBatchSize = 2000;

    /// <summary>The JS namespace of the Leaflet render modes (<c>geoassets.js</c>).</summary>
    public const string LeafletNamespace = "GeoAssets";

    /// <summary>The JS namespace of <see cref="MapRenderMode.MapLibre"/> (<c>geoassets.maplibre.js</c>).</summary>
    public const string MapLibreNamespace = "GeoAssetsMapLibre";

    /// <summary>Where <c>geoassets.maplibre.js</c> is imported from (the RCL's static web assets).</summary>
    public const string MapLibreModuleUrl = "./_content/GeoAssets.Shared/js/geoassets.maplibre.js";

    private readonly IJSRuntime _js;
    private readonly IAssetProvider _repo;
    private readonly MapInteropOptions _options;
    private readonly MapLibreOptions _mapLibreOptions;
    private readonly bool _mapLibre;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private MapTileStyleRefresher? _styleRefresher;
    private Task? _mapLibreLoad;
    private readonly Dictionary<(string DivId, string LayerId), DotNetObjectReference<LocalRasterTileReader>> _localRasterReaders = [];

    /// <summary>
    /// Compact (non-indented) options for the JS interop path.
    /// Mirrors GeoJsonSerializer.Options but without WriteIndented.
    /// </summary>
    private static readonly JsonSerializerOptions _interopOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new GeoGeometryConverter() }
    };

    /// <param name="timeProvider">The MapLibre mode's refresh clock; the system clock when none is registered.</param>
    /// <param name="routes">The gateway a relative <see cref="MapLibreOptions.StyleUrl"/> is under; the page's base URI
    /// when none is registered.</param>
    /// <param name="logger">Where the MapLibre mode reports a failing style refresh.</param>
    public MapInteropService(
        IJSRuntime js,
        IAssetProvider repo,
        IOptions<MapInteropOptions> options,
        TimeProvider? timeProvider = null,
        ApiScopeRoutes? routes = null,
        ILogger<MapInteropService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _js = js;
        _repo = repo;
        _options = options.Value;
        _mapLibre = _options.RenderMode == MapRenderMode.MapLibre;
        _mapLibreOptions = _options.MapLibre ?? new MapLibreOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        TileStyleUrl = MapLibreOptions.ResolveStyleUrl(_mapLibreOptions.StyleUrl, routes?.BaseUrl);
        if (!_mapLibre) return;

        MapLibreMap = new MapLibreMapOptions(TileStyleUrl, routes?.BaseUrl.AbsoluteUri);
        EnsureStyleRefresher();
    }

    /// <summary>What a MapLibre map is initialized with; <c>null</c> in the other modes.</summary>
    public MapLibreMapOptions? MapLibreMap { get; }

    /// <summary>The kernel's tile style (<see cref="MapLibreOptions.ResolveStyleUrl"/>), in every mode.</summary>
    public string TileStyleUrl { get; }

    public event EventHandler<MapTileStyleRefreshedEventArgs>? TileStyleRefreshed;

    public async Task InitializeMapAsync(string divId, double lat, double lon, int zoom)
    {
        var mode = _options.RenderMode.ToString().ToUpperInvariant();
        if (!_mapLibre)
        {
            await CallAsync("initializeMap", divId, lat, lon, zoom, mode);
            return;
        }

        await CallAsync("initializeMap", divId, lat, lon, zoom, mode, MapLibreMap);
        _styleRefresher!.Attach(divId);
    }

    public Task DestroyMapAsync(string divId)
    {
        _styleRefresher?.Detach(divId);
        foreach (var key in _localRasterReaders.Keys.Where(k => string.Equals(k.DivId, divId, StringComparison.Ordinal)).ToList())
        {
            _localRasterReaders.Remove(key, out var reader);
            reader?.Dispose();
        }

        return CallAsync("destroyMap", divId);
    }

    public Task InvalidateSizeAsync(string divId) =>
        CallAsync("invalidateSize", divId);

    public Task SetViewAsync(string divId, double lat, double lon, int zoom) =>
        CallAsync("setView", divId, lat, lon, zoom);

    public Task RenderFeatureAsync(string divId, GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var json = JsonSerializer.Serialize(feature, _interopOptions);
        var colorMap = BuildColorMap();
        var style = BuildStyleMap(_repo, [feature]).GetValueOrDefault(feature.Id);
        return CallAsync("renderFeature", divId, json, colorMap, style);
    }

    public async Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features)
    {
        var featureList = features as IReadOnlyList<GeoFeature> ?? [.. features];
        var styleMap = BuildStyleMap(_repo, featureList);
        var featuresAsJsonString = featureList.Select(f => JsonSerializer.Serialize(f, _interopOptions)).ToList();
        await CallAsync("clearAllFeatures", divId);
        await RenderFeatureBatchesAsync(divId, featuresAsJsonString, featureList.Select(f => f.Id).ToList(), styleMap);
    }

    public Task RenderFeatureBatchAsync(string divId, IEnumerable<GeoFeature> features)
    {
        var featureList = features as IReadOnlyList<GeoFeature> ?? [.. features];
        var styleMap = BuildStyleMap(_repo, featureList);
        var featuresAsJsonString = featureList.Select(f => JsonSerializer.Serialize(f, _interopOptions)).ToList();
        return RenderFeatureBatchesAsync(divId, featuresAsJsonString, featureList.Select(f => f.Id).ToList(), styleMap);
    }

    /// <summary>
    /// Renders features from pre-serialized <see cref="JsonElement"/> objects. Each feature's
    /// <see cref="Layer"/> is resolved via <see cref="LayerResolver"/> from its id and properties
    /// alone (<see cref="FeatureStyleKeys"/>, XD01-429) — never its geometry — and only when the
    /// repository has a layer any feature could resolve to.
    /// </summary>
    public async Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var featuresAsJsonString = features.Select(f => f.GetRawText()).ToList();
        var keys = StyleKeys(() => [.. features.Select(FeatureStyleKeys.Read)]);
        await CallAsync("clearAllFeatures", divId);
        await RenderFeatureBatchesAsync(divId, featuresAsJsonString, keys?.Select(f => f.Id).ToList(), StyleMapOf(keys));
    }

    public Task RenderFeatureBatchAsync(string divId, IReadOnlyList<JsonElement> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var featuresAsJsonString = features.Select(f => f.GetRawText()).ToList();
        var keys = StyleKeys(() => [.. features.Select(FeatureStyleKeys.Read)]);
        return RenderFeatureBatchesAsync(divId, featuresAsJsonString, keys?.Select(f => f.Id).ToList(), StyleMapOf(keys));
    }

    public async Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson)
    {
        var colorMap = BuildColorMap();
        var styleMap = RawStyleMap(rawFeaturesJson);
        await CallAsync("clearAllFeatures", divId);
        // Pass the raw JSON string directly — JS parses it natively via JSON.parse, avoiding WASM parsing entirely.
        await CallAsync("renderFeatureBatch", divId, rawFeaturesJson, colorMap, styleMap);
    }

    /// <summary>
    /// Same JS call as <see cref="RenderAllFeaturesRawJsonAsync"/>, minus the clearAllFeatures —
    /// additive, safe to call once per chunk while progressively
    /// streaming a large result. The chunk's styles travel with it (XD01-429: the raw pan path used to
    /// drop them): resolved from each feature's id and properties (<see cref="FeatureStyleKeys"/>), and
    /// only when the repository has a layer any feature could resolve to — otherwise the string goes
    /// to JS unread, as before.
    /// </summary>
    public Task RenderFeatureBatchRawJsonAsync(string divId, string rawFeaturesJson)
    {
        var colorMap = BuildColorMap();
        var styleMap = RawStyleMap(rawFeaturesJson);
        return CallAsync("renderFeatureBatch", divId, rawFeaturesJson, colorMap, styleMap);
    }

    public Task<int> BeginViewportRenderAsync(string divId) =>
        CallAsync<int>("beginViewportRender", divId);

    public Task EndViewportRenderAsync(string divId, int generation) =>
        CallAsync("endViewportRender", divId, generation);

    /// <summary>
    /// Sends the features to JS, in one call (<see cref="MapInteropOptions.SinglePass"/>) or in
    /// <see cref="MapInteropOptions.BatchSize"/> batches. Each call carries only its own features'
    /// styles (XD01-429 — the whole map used to be re-sent with every batch); none when the batch
    /// has no resolved style. <paramref name="featureIds"/> lines up with
    /// <paramref name="featuresAsJsonString"/> and is only read when there is a <paramref name="styleMap"/>.
    /// </summary>
    private async Task RenderFeatureBatchesAsync(
        string divId,
        List<string> featuresAsJsonString,
        IReadOnlyList<string>? featureIds,
        IReadOnlyDictionary<string, LayerStyleOptions>? styleMap)
    {
        var colorMap = BuildColorMap();

        if (_options.SinglePass)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < featuresAsJsonString.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(featuresAsJsonString[i]);
            }
            sb.Append(']');
            await CallAsync("renderFeatureBatch", divId, sb.ToString(), colorMap,
                StylesFor(styleMap, featureIds, 0, featuresAsJsonString.Count));
        }
        else
        {
            int batchSize = _options.BatchSize;
            for (int i = 0; i < featuresAsJsonString.Count; i += batchSize)
            {
                var count = Math.Min(batchSize, featuresAsJsonString.Count - i);
                var sb = new StringBuilder("[");
                for (int j = 0; j < count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(featuresAsJsonString[i + j]);
                }
                sb.Append(']');
                await CallAsync("renderFeatureBatch", divId, sb.ToString(), colorMap,
                    StylesFor(styleMap, featureIds, i, count));
                await Task.Delay(1); // yield to the browser event loop between batches
            }
        }
    }

    /// <summary>
    /// The styles of the features <paramref name="start"/> … <paramref name="start"/> + <paramref name="count"/> − 1
    /// of <paramref name="featureIds"/> (XD01-429); <c>null</c> when none of them has one, so a batch
    /// never carries another batch's styles.
    /// </summary>
    public static Dictionary<string, LayerStyleOptions>? StylesFor(
        IReadOnlyDictionary<string, LayerStyleOptions>? styleMap, IReadOnlyList<string>? featureIds, int start, int count)
    {
        if (styleMap is null || featureIds is null || styleMap.Count == 0) return null;

        Dictionary<string, LayerStyleOptions>? batch = null;
        for (var i = start; i < start + count; i++)
        {
            if (styleMap.TryGetValue(featureIds[i], out var style))
                (batch ??= new Dictionary<string, LayerStyleOptions>(StringComparer.Ordinal))[featureIds[i]] = style;
        }
        return batch;
    }

    /// <summary>The style keys <paramref name="read"/> produces, or <c>null</c> — without reading
    /// anything — when the repository has no layer, since then no feature resolves a style.</summary>
    private IReadOnlyList<GeoFeature>? StyleKeys(Func<IReadOnlyList<GeoFeature>> read) =>
        _repo.GetLayers().Count == 0 ? null : read();

    private Dictionary<string, LayerStyleOptions>? StyleMapOf(IReadOnlyList<GeoFeature>? keys) =>
        keys is null ? null : NullIfEmpty(BuildStyleMap(_repo, keys));

    private Dictionary<string, LayerStyleOptions>? RawStyleMap(string rawFeaturesJson)
    {
        ArgumentNullException.ThrowIfNull(rawFeaturesJson);
        return StyleMapOf(StyleKeys(() => FeatureStyleKeys.Read(rawFeaturesJson)));
    }

    private static Dictionary<string, LayerStyleOptions>? NullIfEmpty(Dictionary<string, LayerStyleOptions> map) =>
        map.Count == 0 ? null : map;

    public Task RemoveFeatureAsync(string divId, string featureId) =>
        CallAsync("removeFeature", divId, featureId);

    public Task ClearAllFeaturesAsync(string divId) =>
        CallAsync("clearAllFeatures", divId);

    public Task EnableDrawModeAsync(string divId, GeometryType mode) =>
        CallAsync("enableDraw", divId, mode.ToString());

    public Task DisableDrawModeAsync(string divId) =>
        CallAsync("disableDraw", divId);

    public Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds) =>
        CallAsync("setSnapTargetLayer", divId, allowedTargetAssetTypeIds);

    public Task ClearSnapTargetScopeAsync(string divId) =>
        CallAsync("clearSnapTargetScope", divId);

    public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) =>
        CallAsync("addTileLayer", divId, layerId, url, options);

    public Task RemoveTileLayerAsync(string divId, string layerId) =>
        CallAsync("removeTileLayer", divId, layerId);

    public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options) =>
        CallAsync("addWmsLayer", divId, layerId, wmsBaseUrl, options);

    public Task RemoveWmsLayerAsync(string divId, string layerId) =>
        CallAsync("removeWmsLayer", divId, layerId);

    public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible) =>
        CallAsync("setLayerVisibility", divId, assetTypeId, visible);

    public Task SetLayerZoomRangeAsync(string divId, string assetTypeId, int? minZoom, int? maxZoom)
    {
        AssetTypeZoomRange.EnsureValid(minZoom, maxZoom);
        return CallAsync("setLayerZoomRange", divId, assetTypeId, minZoom, maxZoom);
    }

    public Task SetReferenceLayerAsync(string divId, string layerId, string rawFeaturesJson, ReferenceLayerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(rawFeaturesJson);
        ReferenceLayerOptions.EnsureValid(options);
        // Forwarded as-is, like the raw viewport path: JS parses it natively.
        return CallAsync("setReferenceLayer", divId, layerId, rawFeaturesJson, options);
    }

    public Task ClearReferenceLayerAsync(string divId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        return CallAsync("clearReferenceLayer", divId, layerId);
    }

    public async Task<BoundingBox?> GetViewBoundsAsync(string divId)
    {
        var bounds = await CallAsync<double[]?>("getViewBounds", divId);
        return bounds is { Length: 4 } ? new BoundingBox(bounds[0], bounds[1], bounds[2], bounds[3]) : null;
    }

    public Task FitBoundsAsync(string divId, double[] bbox) =>
        CallAsync("fitBounds", divId, bbox);

    public Task PanToFeatureAsync(string divId, string featureId) =>
        CallAsync("panToFeature", divId, featureId);

    public Task HighlightFeatureAsync(string divId, string featureId) =>
        CallAsync("highlightFeature", divId, featureId);

    public Task ClearHighlightAsync(string divId, string featureId) =>
        CallAsync("clearHighlight", divId, featureId);

    public async Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        if (featureIds.Count == 0) return;

        var stateName = state.ToJsName();
        foreach (var batch in featureIds.Chunk(FeatureStateBatchSize))
            await CallAsync("setFeatureStates", divId, batch, stateName);
    }

    public Task ClearFeatureStatesAsync(string divId) =>
        CallAsync("clearFeatureStates", divId);

    public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) =>
        CallAsync("setSelectionTool", divId, tool.ToJsName());

    public Task ShowSelectionOutlineAsync(string divId, double[][][] rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        return CallAsync("showSelectionOutline", divId, rings);
    }

    public Task ClearSelectionOutlineAsync(string divId) =>
        CallAsync("clearSelectionOutline", divId);

    public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) =>
        CallAsync("registerHandlers", divId, handlerRef);

    // ── Kernel tile layers (XD01-436) ─────────────────────────────────────────

    public async Task<MapTileStyleInfo?> ShowTileLayersAsync(string divId)
    {
        var info = await CallAsync<MapTileStyleInfo?>("showTileLayers", divId, TileStyleUrl);
        // A MapLibre map follows {v} from its initialization; a Leaflet map only while its tile layer is shown.
        if (!_mapLibre) EnsureStyleRefresher().Attach(divId);
        return info;
    }

    public Task HideTileLayersAsync(string divId)
    {
        if (!_mapLibre) _styleRefresher?.Detach(divId);
        return CallAsync("hideTileLayers", divId);
    }

    public Task SetHiddenTileFeaturesAsync(string divId, IReadOnlyCollection<string> featureIds)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        // A raster tile can't hide one feature: the Leaflet split has nothing to filter.
        return _mapLibre ? CallAsync("setHiddenTileFeatures", divId, featureIds) : Task.CompletedTask;
    }

    // ── Local raster tile layers (XD01-465) ───────────────────────────────────

    /// <inheritdoc />
    /// <remarks>Leaflet modes only: in <see cref="MapRenderMode.MapLibre"/> (the Web, which has no local packs) nothing is drawn.</remarks>
    public async Task AddLocalRasterTileLayerAsync(string divId, string layerId, IRasterTileSource tiles, LocalRasterTileLayer layer)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(layer);
        if (_mapLibre) return;

        // Asked again (every bulk render asks), the layer keeps its reader; JS adds it only when the map has none.
        var added = !_localRasterReaders.TryGetValue((divId, layerId), out var reader);
        if (added)
        {
            reader = DotNetObjectReference.Create(new LocalRasterTileReader(tiles, layer));
            _localRasterReaders[(divId, layerId)] = reader;
        }

        try
        {
            await CallAsync("addLocalRasterLayer", divId, layerId, reader, new
            {
                contentType = layer.ContentType,
                minZoom = layer.MinZoom,
                maxZoom = layer.MaxZoom,
                bounds = layer.Bounds is { } b ? new[] { b.MinLon, b.MinLat, b.MaxLon, b.MaxLat } : null,
            });
        }
        catch
        {
            if (added)
            {
                _localRasterReaders.Remove((divId, layerId));
                reader!.Dispose();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveLocalRasterTileLayerAsync(string divId, string layerId)
    {
        if (!_localRasterReaders.Remove((divId, layerId), out var reader)) return;

        try
        {
            await CallAsync("removeLocalRasterLayer", divId, layerId);
        }
        finally
        {
            reader.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var reader in _localRasterReaders.Values)
            reader.Dispose();
        _localRasterReaders.Clear();

        if (_styleRefresher is null) return ValueTask.CompletedTask;

        _repo.FeatureAdded -= OnFeatureWritten;
        _repo.FeatureUpdated -= OnFeatureWritten;
        _repo.FeatureDeleted -= OnFeatureDeleted;
        _repo.CollectionChanged -= OnCollectionChanged;
        _styleRefresher.Dispose();
        return ValueTask.CompletedTask;
    }

    // ── JS dispatch ───────────────────────────────────────────────────────────

    /// <summary>Calls <paramref name="function"/> of the render mode's JS namespace — in MapLibre mode once
    /// <c>geoassets.maplibre.js</c> is loaded.</summary>
    private Task CallAsync(string function, params object?[] args) =>
        _mapLibre
            ? CallMapLibreAsync(function, args)
            : _js.InvokeVoidAsync($"{LeafletNamespace}.{function}", args).AsTask();

    private async Task CallMapLibreAsync(string function, object?[] args)
    {
        await EnsureMapLibreLoadedAsync();
        await _js.InvokeVoidAsync($"{MapLibreNamespace}.{function}", args);
    }

    private async Task<TValue> CallAsync<TValue>(string function, params object?[] args)
    {
        if (!_mapLibre)
            return await _js.InvokeAsync<TValue>($"{LeafletNamespace}.{function}", args);

        await EnsureMapLibreLoadedAsync();
        return await _js.InvokeAsync<TValue>($"{MapLibreNamespace}.{function}", args);
    }

    /// <summary>Imports <see cref="MapLibreModuleUrl"/> once; a failed import is retried by the next call.</summary>
    private async Task EnsureMapLibreLoadedAsync()
    {
        var load = _mapLibreLoad ??= _js.InvokeVoidAsync("import", MapLibreModuleUrl).AsTask();
        try
        {
            await load;
        }
        catch
        {
            if (ReferenceEquals(_mapLibreLoad, load)) _mapLibreLoad = null;
            throw;
        }
    }

    /// <summary>The refresher of the tile style — created, and subscribed to the provider's writes, on first need.</summary>
    private MapTileStyleRefresher EnsureStyleRefresher()
    {
        if (_styleRefresher is { } existing) return existing;
        _styleRefresher = new MapTileStyleRefresher(RefreshTileStyleAsync, _mapLibreOptions, _timeProvider, _logger);
        _repo.FeatureAdded += OnFeatureWritten;
        _repo.FeatureUpdated += OnFeatureWritten;
        _repo.FeatureDeleted += OnFeatureDeleted;
        _repo.CollectionChanged += OnCollectionChanged;
        return _styleRefresher;
    }

    private async Task RefreshTileStyleAsync(string divId, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        string? version;
        if (_mapLibre)
        {
            await EnsureMapLibreLoadedAsync();
            version = await _js.InvokeAsync<string?>($"{MapLibreNamespace}.refreshTileStyle", cancellationToken, [divId]);
        }
        else
        {
            version = await _js.InvokeAsync<string?>($"{LeafletNamespace}.refreshTileStyle", cancellationToken, [divId]);
        }
        TileStyleRefreshed?.Invoke(this, new MapTileStyleRefreshedEventArgs(divId, version, startedAt));
    }

    private void OnFeatureWritten(object? sender, GeoFeature feature) => _styleRefresher?.NotifyWrite();

    private void OnFeatureDeleted(object? sender, string featureId) => _styleRefresher?.NotifyWrite();

    private void OnCollectionChanged(object? sender, EventArgs e) => _styleRefresher?.NotifyWrite();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a string-keyed lookup of assetTypeId → color.
    /// Called once per render operation; Guid.ToString() is paid per type, not per feature.
    /// </summary>
    private Dictionary<string, string> BuildColorMap() =>
        _repo.GetAssetTypes().ToDictionary(t => t.Id.ToString(), t => t.Color, StringComparer.Ordinal);

    /// <summary>
    /// Resolves each feature's effective <see cref="Layer"/> via <see cref="LayerResolver"/> and
    /// builds a featureId → style lookup for the JS render path. A feature is omitted from the map
    /// (rather than mapped to a fallback entry) when its asset type is unknown or
    /// <see cref="LayerResolver.Resolve"/> returns <c>null</c> — <c>geoassets.js</c> falls back to
    /// the pre-existing per-asset-type default style (via <c>colorMap</c>) for anything missing here.
    /// Takes <paramref name="repo"/> explicitly (rather than closing over <c>_repo</c>) so it's
    /// directly unit-testable without an <see cref="IJSRuntime"/> — this service has no other
    /// test coverage today, same reasoning as <c>LayerResolver</c> and <c>NavMenu.FilterByPermissionAsync</c>.
    /// </summary>
    public static Dictionary<string, LayerStyleOptions> BuildStyleMap(IAssetProvider repo, IEnumerable<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(features);
        var assetTypesById = repo.GetAssetTypes().ToDictionary(t => t.Id.ToString(), StringComparer.Ordinal);
        var layers = repo.GetLayers();
        var ruleCache = new Dictionary<Guid, IReadOnlyList<LayerRule>>();
        var map = new Dictionary<string, LayerStyleOptions>(StringComparer.Ordinal);

        foreach (var feature in features)
        {
            if (!assetTypesById.TryGetValue(feature.Properties.AssetTypeId, out var assetType))
                continue;

            if (!ruleCache.TryGetValue(assetType.Id, out var rules))
                ruleCache[assetType.Id] = rules = repo.GetLayerRules(assetType.Id);

            var layer = LayerResolver.Resolve(feature, assetType, layers, rules);
            if (layer is not null)
                map[feature.Id] = ToJsStyle(layer);
        }

        return map;
    }

    private static LayerStyleOptions ToJsStyle(Layer layer) => new(
        Color: layer.Color,
        Weight: layer.Weight,
        Radius: layer.Radius,
        FillColor: layer.FillColor,
        FillOpacity: layer.FillOpacity,
        DashArray: string.IsNullOrEmpty(layer.DashArray) ? null : layer.DashArray,
        IconUrl: string.IsNullOrEmpty(layer.IconUrl) ? null : layer.IconUrl);
}
