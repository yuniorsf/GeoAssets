using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Interfaces;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// <see cref="IMapInterop"/> double for the layer-display and view tests (XD01-311): records
/// <see cref="FitBoundsAsync"/>, <see cref="SetLayerVisibilityAsync"/> and
/// <see cref="SetLayerZoomRangeAsync"/>; every other member throws, so a test that completes
/// proves nothing else on the map was touched.
/// </summary>
internal sealed class RecordingMapInterop : IMapInterop
{
    public List<(string DivId, double[] Bbox)> FitCalls { get; } = [];
    public List<(string DivId, string AssetTypeId, bool Visible)> VisibilityCalls { get; } = [];
    public List<(string DivId, string AssetTypeId, int? MinZoom, int? MaxZoom)> ZoomRangeCalls { get; } = [];

    /// <summary>Asset type ids whose <see cref="SetLayerZoomRangeAsync"/> call fails.</summary>
    public HashSet<string> FailZoomRangeFor { get; } = new(StringComparer.Ordinal);

    public Task FitBoundsAsync(string divId, double[] bbox)
    {
        FitCalls.Add((divId, bbox));
        return Task.CompletedTask;
    }

    public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible)
    {
        VisibilityCalls.Add((divId, assetTypeId, visible));
        return Task.CompletedTask;
    }

    public Task SetLayerZoomRangeAsync(string divId, string assetTypeId, int? minZoom, int? maxZoom)
    {
        if (FailZoomRangeFor.Contains(assetTypeId)) return Task.FromException(new JSException("boom"));
        ZoomRangeCalls.Add((divId, assetTypeId, minZoom, maxZoom));
        return Task.CompletedTask;
    }

    public Task InitializeMapAsync(string divId, double lat, double lon, int zoom) => throw new NotSupportedException();
    public Task DestroyMapAsync(string divId) => throw new NotSupportedException();
    public Task InvalidateSizeAsync(string divId) => throw new NotSupportedException();
    public Task SetViewAsync(string divId, double lat, double lon, int zoom) => throw new NotSupportedException();
    public Task RenderFeatureAsync(string divId, GeoFeature feature) => throw new NotSupportedException();
    public Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features) => throw new NotSupportedException();
    public Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features) => throw new NotSupportedException();
    public Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson) => throw new NotSupportedException();
    public Task RenderFeatureBatchRawJsonAsync(string divId, string rawFeaturesJson) => throw new NotSupportedException();
    public Task RenderFeatureBatchAsync(string divId, IEnumerable<GeoFeature> features) => throw new NotSupportedException();
    public Task RenderFeatureBatchAsync(string divId, IReadOnlyList<JsonElement> features) => throw new NotSupportedException();
    public Task RemoveFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
    public Task ClearAllFeaturesAsync(string divId) => throw new NotSupportedException();
    public Task EnableDrawModeAsync(string divId, GeometryType mode) => throw new NotSupportedException();
    public Task DisableDrawModeAsync(string divId) => throw new NotSupportedException();
    public Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds) => throw new NotSupportedException();
    public Task ClearSnapTargetScopeAsync(string divId) => throw new NotSupportedException();
    public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) => throw new NotSupportedException();
    public Task RemoveTileLayerAsync(string divId, string layerId) => throw new NotSupportedException();
    public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options) => throw new NotSupportedException();
    public Task RemoveWmsLayerAsync(string divId, string layerId) => throw new NotSupportedException();
    public Task PanToFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
    public Task HighlightFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
    public Task ClearHighlightAsync(string divId, string featureId) => throw new NotSupportedException();
    public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) => throw new NotSupportedException();
}
