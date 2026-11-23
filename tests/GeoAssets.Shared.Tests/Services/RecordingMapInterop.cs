using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// <see cref="IMapInterop"/> double for the layer-display and view tests (XD01-311): records
/// <see cref="FitBoundsAsync"/>, <see cref="SetLayerVisibilityAsync"/> and
/// <see cref="SetLayerZoomRangeAsync"/>, and the reference-layer calls (XD01-331:
/// <see cref="SetReferenceLayerAsync"/>, <see cref="ClearReferenceLayerAsync"/>,
/// <see cref="GetViewBoundsAsync"/>); every other member throws, so a test that completes
/// proves nothing else on the map was touched — no feature rendered, removed or cleared.
/// </summary>
internal sealed class RecordingMapInterop : IMapInterop
{
    public List<(string DivId, double[] Bbox)> FitCalls { get; } = [];
    public List<(string DivId, string AssetTypeId, bool Visible)> VisibilityCalls { get; } = [];
    public List<(string DivId, string AssetTypeId, int? MinZoom, int? MaxZoom)> ZoomRangeCalls { get; } = [];
    public List<(string DivId, string LayerId, string Json, ReferenceLayerOptions Options)> ReferenceSetCalls { get; } = [];
    public List<(string DivId, string LayerId)> ReferenceClearCalls { get; } = [];
    public List<string> ViewBoundsCalls { get; } = [];

    /// <summary>Asset type ids whose <see cref="SetLayerZoomRangeAsync"/> call fails.</summary>
    public HashSet<string> FailZoomRangeFor { get; } = new(StringComparer.Ordinal);

    /// <summary>What <see cref="GetViewBoundsAsync"/> answers.</summary>
    public BoundingBox? ViewBounds { get; set; }

    /// <summary>When set, <see cref="SetReferenceLayerAsync"/> fails with it.</summary>
    public Exception? FailReferenceSetWith { get; set; }

    public Task SetReferenceLayerAsync(string divId, string layerId, string rawFeaturesJson, ReferenceLayerOptions options)
    {
        if (FailReferenceSetWith is { } failure) return Task.FromException(failure);
        ReferenceSetCalls.Add((divId, layerId, rawFeaturesJson, options));
        return Task.CompletedTask;
    }

    public Task ClearReferenceLayerAsync(string divId, string layerId)
    {
        ReferenceClearCalls.Add((divId, layerId));
        return Task.CompletedTask;
    }

    public Task<BoundingBox?> GetViewBoundsAsync(string divId)
    {
        ViewBoundsCalls.Add(divId);
        return Task.FromResult(ViewBounds);
    }

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
