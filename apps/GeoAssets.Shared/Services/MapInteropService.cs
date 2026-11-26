using GeoAssets.Shared.Interfaces;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Implements IMapInterop and IMapSelectionInterop by delegating every call to window.GeoAssets.*
/// functions defined in geoassets.js.
/// </summary>
public sealed class MapInteropService : IMapInterop, IMapSelectionInterop, IAsyncDisposable
{
    /// <summary>Maximum feature ids sent to <c>GeoAssets.setFeatureStates</c> in one JS call (XD01-309).</summary>
    public const int FeatureStateBatchSize = 2000;

    private readonly IJSRuntime _js;
    private readonly IAssetProvider _repo;
    private readonly MapInteropOptions _options;
    private const string Ns = "GeoAssets"; // window.GeoAssets

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

    public MapInteropService(IJSRuntime js, IAssetProvider repo, IOptions<MapInteropOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _js = js;
        _repo = repo;
        _options = options.Value;
    }

    public Task InitializeMapAsync(string divId, double lat, double lon, int zoom) =>
        _js.InvokeVoidAsync($"{Ns}.initializeMap", divId, lat, lon, zoom,
            _options.RenderMode.ToString().ToUpperInvariant()).AsTask();

    public Task DestroyMapAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.destroyMap", divId).AsTask();

    public Task InvalidateSizeAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.invalidateSize", divId).AsTask();

    public Task SetViewAsync(string divId, double lat, double lon, int zoom) =>
        _js.InvokeVoidAsync($"{Ns}.setView", divId, lat, lon, zoom).AsTask();

    public Task RenderFeatureAsync(string divId, GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var json = JsonSerializer.Serialize(feature, _interopOptions);
        var colorMap = BuildColorMap();
        var style = BuildStyleMap(_repo, [feature]).GetValueOrDefault(feature.Id);
        return _js.InvokeVoidAsync($"{Ns}.renderFeature", divId, json, colorMap, style).AsTask();
    }

    public async Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features)
    {
        var featureList = features as IReadOnlyList<GeoFeature> ?? [.. features];
        var styleMap = BuildStyleMap(_repo, featureList);
        var featuresAsJsonString = featureList.Select(f => JsonSerializer.Serialize(f, _interopOptions)).ToList();
        await _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId);
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
        await _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId);
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
        await _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId);
        // Pass the raw JSON string directly — JS parses it natively via JSON.parse, avoiding WASM parsing entirely.
        await _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, rawFeaturesJson, colorMap, styleMap);
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
        return _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, rawFeaturesJson, colorMap, styleMap).AsTask();
    }

    public async Task<int> BeginViewportRenderAsync(string divId) =>
        await _js.InvokeAsync<int>($"{Ns}.beginViewportRender", divId);

    public Task EndViewportRenderAsync(string divId, int generation) =>
        _js.InvokeVoidAsync($"{Ns}.endViewportRender", divId, generation).AsTask();

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
            await _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, sb.ToString(), colorMap,
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
                await _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, sb.ToString(), colorMap,
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
        _js.InvokeVoidAsync($"{Ns}.removeFeature", divId, featureId).AsTask();

    public Task ClearAllFeaturesAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId).AsTask();

    public Task EnableDrawModeAsync(string divId, GeometryType mode) =>
        _js.InvokeVoidAsync($"{Ns}.enableDraw", divId, mode.ToString()).AsTask();

    public Task DisableDrawModeAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.disableDraw", divId).AsTask();

    public Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds) =>
        _js.InvokeVoidAsync($"{Ns}.setSnapTargetLayer", divId, allowedTargetAssetTypeIds).AsTask();

    public Task ClearSnapTargetScopeAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.clearSnapTargetScope", divId).AsTask();

    public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) =>
        _js.InvokeVoidAsync($"{Ns}.addTileLayer", divId, layerId, url, options).AsTask();

    public Task RemoveTileLayerAsync(string divId, string layerId) =>
        _js.InvokeVoidAsync($"{Ns}.removeTileLayer", divId, layerId).AsTask();

    public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options) =>
        _js.InvokeVoidAsync($"{Ns}.addWmsLayer", divId, layerId, wmsBaseUrl, options).AsTask();

    public Task RemoveWmsLayerAsync(string divId, string layerId) =>
        _js.InvokeVoidAsync($"{Ns}.removeWmsLayer", divId, layerId).AsTask();

    public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible) =>
        _js.InvokeVoidAsync($"{Ns}.setLayerVisibility", divId, assetTypeId, visible).AsTask();

    public Task SetLayerZoomRangeAsync(string divId, string assetTypeId, int? minZoom, int? maxZoom)
    {
        AssetTypeZoomRange.EnsureValid(minZoom, maxZoom);
        return _js.InvokeVoidAsync($"{Ns}.setLayerZoomRange", divId, assetTypeId, minZoom, maxZoom).AsTask();
    }

    public Task SetReferenceLayerAsync(string divId, string layerId, string rawFeaturesJson, ReferenceLayerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(rawFeaturesJson);
        ReferenceLayerOptions.EnsureValid(options);
        // Forwarded as-is, like the raw viewport path: JS parses it natively.
        return _js.InvokeVoidAsync($"{Ns}.setReferenceLayer", divId, layerId, rawFeaturesJson, options).AsTask();
    }

    public Task ClearReferenceLayerAsync(string divId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        return _js.InvokeVoidAsync($"{Ns}.clearReferenceLayer", divId, layerId).AsTask();
    }

    public async Task<BoundingBox?> GetViewBoundsAsync(string divId)
    {
        var bounds = await _js.InvokeAsync<double[]?>($"{Ns}.getViewBounds", divId);
        return bounds is { Length: 4 } ? new BoundingBox(bounds[0], bounds[1], bounds[2], bounds[3]) : null;
    }

    public Task FitBoundsAsync(string divId, double[] bbox) =>
        _js.InvokeVoidAsync($"{Ns}.fitBounds", divId, bbox).AsTask();

    public Task PanToFeatureAsync(string divId, string featureId) =>
        _js.InvokeVoidAsync($"{Ns}.panToFeature", divId, featureId).AsTask();

    public Task HighlightFeatureAsync(string divId, string featureId) =>
        _js.InvokeVoidAsync($"{Ns}.highlightFeature", divId, featureId).AsTask();

    public Task ClearHighlightAsync(string divId, string featureId) =>
        _js.InvokeVoidAsync($"{Ns}.clearHighlight", divId, featureId).AsTask();

    public async Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        if (featureIds.Count == 0) return;

        var stateName = state.ToJsName();
        foreach (var batch in featureIds.Chunk(FeatureStateBatchSize))
            await _js.InvokeVoidAsync($"{Ns}.setFeatureStates", divId, batch, stateName);
    }

    public Task ClearFeatureStatesAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.clearFeatureStates", divId).AsTask();

    public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) =>
        _js.InvokeVoidAsync($"{Ns}.setSelectionTool", divId, tool.ToJsName()).AsTask();

    public Task ShowSelectionOutlineAsync(string divId, double[][][] rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        return _js.InvokeVoidAsync($"{Ns}.showSelectionOutline", divId, rings).AsTask();
    }

    public Task ClearSelectionOutlineAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.clearSelectionOutline", divId).AsTask();

    public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) =>
        _js.InvokeVoidAsync($"{Ns}.registerHandlers", divId, handlerRef).AsTask();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
