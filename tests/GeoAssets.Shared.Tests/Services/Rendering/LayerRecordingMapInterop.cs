using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// <see cref="IMapInterop"/> double for the provider layer renderers (XD01-422): records the WMS
/// layer calls, the feature batch renders (all three overloads), the feature removals and the
/// view fits; every other member throws, so a test that completes proves nothing else on the map
/// was touched. Thread-safe — the vector renderer's reads run on <see cref="Core.Services.FeatureRenderPipeline"/>'s
/// background producer, and its awaits may resume off the test's thread.
/// </summary>
internal sealed class LayerRecordingMapInterop : IMapInterop
{
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _fitted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(string DivId, string LayerId, string BaseUrl, WmsLayerOptions Options)> WmsAdds { get; } = [];
    public List<(string DivId, string LayerId)> WmsRemoves { get; } = [];
    public List<(string DivId, IReadOnlyList<GeoFeature> Features)> FeatureBatches { get; } = [];
    public List<(string DivId, IReadOnlyList<JsonElement> Features)> JsonBatches { get; } = [];
    public List<(string DivId, string Json)> RawJsonBatches { get; } = [];
    public List<(string DivId, string FeatureId)> RemovedFeatures { get; } = [];
    public List<(string DivId, double[] Bbox)> FitCalls { get; } = [];

    /// <summary>When set, every feature batch render fails with it.</summary>
    public Exception? FailBatchWith { get; init; }

    /// <summary>Completes on the first <see cref="FitBoundsAsync"/> call.</summary>
    public Task Fitted => _fitted.Task;

    /// <summary>Every feature id rendered through <see cref="RenderFeatureBatchAsync(string, IEnumerable{GeoFeature})"/>.</summary>
    public IReadOnlyList<string> RenderedFeatureIds
    {
        get { lock (_gate) return [.. FeatureBatches.SelectMany(b => b.Features).Select(f => f.Id)]; }
    }

    public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options)
    {
        lock (_gate) WmsAdds.Add((divId, layerId, wmsBaseUrl, options));
        return Task.CompletedTask;
    }

    public Task RemoveWmsLayerAsync(string divId, string layerId)
    {
        lock (_gate) WmsRemoves.Add((divId, layerId));
        return Task.CompletedTask;
    }

    public Task RenderFeatureBatchAsync(string divId, IEnumerable<GeoFeature> features)
    {
        if (FailBatchWith is { } failure) return Task.FromException(failure);
        lock (_gate) FeatureBatches.Add((divId, [.. features]));
        return Task.CompletedTask;
    }

    public Task RenderFeatureBatchAsync(string divId, IReadOnlyList<JsonElement> features)
    {
        lock (_gate) JsonBatches.Add((divId, features));
        return Task.CompletedTask;
    }

    public Task RenderFeatureBatchRawJsonAsync(string divId, string rawFeaturesJson)
    {
        lock (_gate) RawJsonBatches.Add((divId, rawFeaturesJson));
        return Task.CompletedTask;
    }

    public Task RemoveFeatureAsync(string divId, string featureId)
    {
        lock (_gate) RemovedFeatures.Add((divId, featureId));
        return Task.CompletedTask;
    }

    public Task FitBoundsAsync(string divId, double[] bbox)
    {
        lock (_gate) FitCalls.Add((divId, bbox));
        _fitted.TrySetResult();
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
    public Task ClearAllFeaturesAsync(string divId) => throw new NotSupportedException();
    public Task EnableDrawModeAsync(string divId, GeometryType mode) => throw new NotSupportedException();
    public Task DisableDrawModeAsync(string divId) => throw new NotSupportedException();
    public Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds) => throw new NotSupportedException();
    public Task ClearSnapTargetScopeAsync(string divId) => throw new NotSupportedException();
    public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) => throw new NotSupportedException();
    public Task RemoveTileLayerAsync(string divId, string layerId) => throw new NotSupportedException();
    public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible) => throw new NotSupportedException();
    public Task SetLayerZoomRangeAsync(string divId, string assetTypeId, int? minZoom, int? maxZoom) => throw new NotSupportedException();
    public Task SetReferenceLayerAsync(string divId, string layerId, string rawFeaturesJson, ReferenceLayerOptions options) => throw new NotSupportedException();
    public Task ClearReferenceLayerAsync(string divId, string layerId) => throw new NotSupportedException();
    public Task<BoundingBox?> GetViewBoundsAsync(string divId) => throw new NotSupportedException();
    public Task PanToFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
    public Task HighlightFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
    public Task ClearHighlightAsync(string divId, string featureId) => throw new NotSupportedException();
    public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) => throw new NotSupportedException();
}
