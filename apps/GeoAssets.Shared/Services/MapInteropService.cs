using GeoAssets.Shared.Interfaces;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Implements IMapInterop by delegating every call to window.GeoAssets.*
/// functions defined in geoassets.js.
/// </summary>
public sealed class MapInteropService : IMapInterop, IAsyncDisposable
{
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
        _js = js;
        _repo = repo;
        _options = options.Value;
    }

    public Task InitializeMapAsync(string divId, double lat, double lon, int zoom) =>
        _js.InvokeVoidAsync($"{Ns}.initializeMap", divId, lat, lon, zoom,
            _options.RenderMode.ToString().ToLowerInvariant()).AsTask();

    public Task DestroyMapAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.destroyMap", divId).AsTask();

    public Task InvalidateSizeAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.invalidateSize", divId).AsTask();

    public Task RenderFeatureAsync(string divId, GeoFeature feature)
    {
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
        await RenderAllFeaturesAsync(divId, featuresAsJsonString, styleMap);
    }

    /// <summary>
    /// Renders features from pre-serialized <see cref="JsonElement"/> objects. Unlike the other
    /// overloads, this one does not resolve each feature's <see cref="Layer"/> via
    /// <see cref="LayerResolver"/> — doing so would require re-parsing the very JSON this overload
    /// exists to avoid round-tripping, and it currently has no caller in the app. Features render
    /// with the generic per-<see cref="AssetType"/> default style, same as before this ticket.
    /// </summary>
    public async Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features)
    {
        var featuresAsJsonString = features.Select(f => f.GetRawText()).ToList();
        await RenderAllFeaturesAsync(divId, featuresAsJsonString);
    }

    public async Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson)
    {
        var colorMap = BuildColorMap();
        await _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId);
        // Pass the raw JSON string directly — JS parses it natively via JSON.parse, avoiding WASM parsing entirely.
        // No per-feature style resolution here either, for the same reason as the JsonElement overload above.
        await _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, rawFeaturesJson, colorMap);
    }

    private async Task RenderAllFeaturesAsync(
        string divId, IReadOnlyList<string> featuresAsJsonString, IReadOnlyDictionary<string, LayerStyleOptions>? styleMap = null)
    {
        var colorMap = BuildColorMap();
        await _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId);

        if (_options.SinglePass)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < featuresAsJsonString.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(featuresAsJsonString[i]);
            }
            sb.Append(']');
            await _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, sb.ToString(), colorMap, styleMap);
        }
        else
        {
            int batchSize = _options.BatchSize;
            for (int i = 0; i < featuresAsJsonString.Count; i += batchSize)
            {
                var sb = new StringBuilder("[");
                for (int j = 0; j < Math.Min(batchSize, featuresAsJsonString.Count - i); j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(featuresAsJsonString[i + j]);
                }
                sb.Append(']');
                await _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, sb.ToString(), colorMap, styleMap);
                await Task.Delay(1); // yield to the browser event loop between batches
            }
        }
    }

    public Task RemoveFeatureAsync(string divId, string featureId) =>
        _js.InvokeVoidAsync($"{Ns}.removeFeature", divId, featureId).AsTask();

    public Task ClearAllFeaturesAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId).AsTask();

    public Task EnableDrawModeAsync(string divId, GeometryType mode) =>
        _js.InvokeVoidAsync($"{Ns}.enableDraw", divId, mode.ToString()).AsTask();

    public Task DisableDrawModeAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.disableDraw", divId).AsTask();

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

    public Task FitBoundsAsync(string divId, double[] bbox) =>
        _js.InvokeVoidAsync($"{Ns}.fitBounds", divId, bbox).AsTask();

    public Task PanToFeatureAsync(string divId, string featureId) =>
        _js.InvokeVoidAsync($"{Ns}.panToFeature", divId, featureId).AsTask();

    public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) =>
        _js.InvokeVoidAsync($"{Ns}.registerHandlers", divId, handlerRef).AsTask();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a string-keyed lookup of assetTypeId → color.
    /// Called once per render operation; Guid.ToString() is paid per type, not per feature.
    /// </summary>
    private Dictionary<string, string> BuildColorMap() =>
        _repo.GetAssetTypes().ToDictionary(t => t.Id.ToString(), t => t.Color);

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
        var assetTypesById = repo.GetAssetTypes().ToDictionary(t => t.Id.ToString());
        var layers = repo.GetLayers();
        var ruleCache = new Dictionary<Guid, IReadOnlyList<LayerRule>>();
        var map = new Dictionary<string, LayerStyleOptions>();

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

/// <summary>
/// A resolved <see cref="Layer"/>'s style fields, shaped for the <c>geoassets.js</c> render path
/// (camelCase JSON via the default JS interop serializer, same as <see cref="TileLayerOptions"/>).
/// </summary>
public sealed record LayerStyleOptions(
    string Color,
    double Weight,
    double Radius,
    string FillColor,
    double FillOpacity,
    string? DashArray,
    string? IconUrl);
