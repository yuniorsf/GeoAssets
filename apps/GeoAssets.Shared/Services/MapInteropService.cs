using GeoAssets.Shared.Interfaces;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
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

    public MapInteropService(IJSRuntime js, IAssetProvider repo)
    {
        _js = js;
        _repo = repo;
    }

    public Task InitializeMapAsync(string divId, double lat, double lon, int zoom) =>
        _js.InvokeVoidAsync($"{Ns}.initializeMap", divId, lat, lon, zoom).AsTask();

    public Task DestroyMapAsync(string divId) =>
        _js.InvokeVoidAsync($"{Ns}.destroyMap", divId).AsTask();

    public Task RenderFeatureAsync(string divId, GeoFeature feature)
    {
        var json = JsonSerializer.Serialize(feature, _interopOptions);
        var colorMap = BuildColorMap();
        return _js.InvokeVoidAsync($"{Ns}.renderFeature", divId, json, colorMap).AsTask();
    }

    public async Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features)
    {
        const int BatchSize = 5;

        // Build the id→color lookup once for the entire render operation.
        // Passed to JS so color resolution happens there — no DOM manipulation in C#.
        var colorMap = BuildColorMap();
        var list = features.ToList();
        await _js.InvokeVoidAsync($"{Ns}.clearAllFeatures", divId);

        for (int i = 0; i < list.Count; i += BatchSize)
        {
            var batch = list.GetRange(i, Math.Min(BatchSize, list.Count - i));

            var sb = new StringBuilder("[");
            for (int j = 0; j < batch.Count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append(JsonSerializer.Serialize(batch[j], _interopOptions));
            }
            sb.Append(']');

            await _js.InvokeVoidAsync($"{Ns}.renderFeatureBatch", divId, sb.ToString(), colorMap);
            await Task.Delay(1); // yield to the browser event loop between batches
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
}
