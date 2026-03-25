using GeoAssets.Shared.Interfaces;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Microsoft.JSInterop;
using System.Text.Json.Nodes;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Implements IMapInterop by delegating every call to window.GeoAssets.*
/// functions defined in geoassets.js.
/// </summary>
public sealed class MapInteropService : IMapInterop, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly IAssetRepository _repo;
    private const string Ns = "GeoAssets"; // window.GeoAssets

    public MapInteropService(IJSRuntime js, IAssetRepository repo)
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
        var json = SerializeWithColor(feature);
        return _js.InvokeVoidAsync($"{Ns}.renderFeature", divId, json).AsTask();
    }

    public Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features)
    {
        var items = features.Select(SerializeWithColor);
        var json  = $"[{string.Join(",", items)}]";
        return _js.InvokeVoidAsync($"{Ns}.renderAllFeatures", divId, json).AsTask();
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
    /// Serializes the feature and injects the resolved asset-type color into
    /// <c>properties.color</c> so the JS renderer can use it directly.
    /// </summary>
    private string SerializeWithColor(GeoFeature feature)
    {
        var color = _repo.GetAssetTypes()
            .FirstOrDefault(t => t.Id.ToString() == feature.Properties.AssetTypeId)
            ?.Color ?? "#3388ff";

        var node = JsonNode.Parse(GeoJsonSerializer.SerializeFeature(feature))!;
        node["properties"]!["color"] = color;
        return node.ToJsonString();
    }
}

