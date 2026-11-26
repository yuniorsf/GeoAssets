using System.Text.Json;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Services.Observability;

/// <summary>
/// Observable decorator for <see cref="IMapInterop"/> and <see cref="IMapSelectionInterop"/>.
/// Instruments <see cref="RenderAllFeaturesAsync"/> with an OpenTelemetry span,
/// a duration metric, and structured logging, and <see cref="SetFeatureStatesAsync"/>,
/// <see cref="SetSelectionToolAsync"/>, <see cref="SetLayerZoomRangeAsync"/>,
/// <see cref="ShowSelectionOutlineAsync"/> and <see cref="SetReferenceLayerAsync"/> with a span; all other calls are forwarded to the inner service without overhead.
/// </summary>
public sealed class ObservableMapInterop(
    IMapInterop inner,
    IMapSelectionInterop selectionInner,
    MapRenderMode renderMode,
    ILogger<ObservableMapInterop> logger,
    TimeProvider timeProvider)
    : ObservableDecoratorBase<ObservableMapInterop>(logger, timeProvider), IMapInterop, IMapSelectionInterop
{
    /// <summary>Span emitted by <see cref="SetFeatureStatesAsync"/> (XD01-309).</summary>
    public const string SetFeatureStatesSpanName = "Map.SetFeatureStates";

    /// <summary>Span emitted by <see cref="SetSelectionToolAsync"/> (XD01-310).</summary>
    public const string SetSelectionToolSpanName = "Map.SetSelectionTool";

    /// <summary>Span emitted by <see cref="SetLayerZoomRangeAsync"/> (XD01-311).</summary>
    public const string SetLayerZoomRangeSpanName = "Map.SetLayerZoomRange";

    /// <summary>Span emitted by <see cref="ShowSelectionOutlineAsync"/> (XD01-311).</summary>
    public const string ShowSelectionOutlineSpanName = "Map.ShowSelectionOutline";

    /// <summary>Span emitted by <see cref="SetReferenceLayerAsync"/> (XD01-331).</summary>
    public const string SetReferenceLayerSpanName = "Map.SetReferenceLayer";

    private readonly string _renderModeTag = renderMode switch
    {
        MapRenderMode.Canvas => "canvas",
        MapRenderMode.WebGL => "webgl",
        _ => "leaflet"
    };

    // ── Instrumented ─────────────────────────────────────────────────────────

    public Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features)
    {
        // Materialise once so Count is available for tags and we pass the same list to inner.
        var list = features as IReadOnlyCollection<GeoFeature> ?? features.ToList();
        return TrackAsync(
            "map.render_all",
            () => inner.RenderAllFeaturesAsync(divId, list),
            before: span => span?.SetTag("feature.count", list.Count)
                                  .SetTag("map.div_id",    divId),
            after: (_, elapsedMs) =>
            {
                ImportDiagnostics.RenderDurationMs.Record(
                    elapsedMs,
                    new KeyValuePair<string, object?>("feature.count", list.Count));
                Logger.LogInformation(
                    "MapInterop.RenderAllFeaturesAsync — {FeatureCount} features in {ElapsedMs} ms",
                    list.Count, elapsedMs);
            });
    }

    public Task RenderFeatureAsync(string divId, GeoFeature feature) =>
        TrackAsync(
            "map.render_feature",
            () => inner.RenderFeatureAsync(divId, feature),
            before: span => span?.SetTag("feature.id", feature.Id)
                                  .SetTag("map.div_id",  divId),
            after: (_, elapsedMs) =>
            {
                ImportDiagnostics.RenderFeatureDurationMs.Record(elapsedMs);
                Logger.LogInformation(
                    "MapInterop.RenderFeatureAsync — feature {FeatureId} in {ElapsedMs} ms",
                    feature.Id, elapsedMs);
            });

    public Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson) =>
        TrackAsync(
            "map.render_all_raw_json",
            () => inner.RenderAllFeaturesRawJsonAsync(divId, rawFeaturesJson),
            before: span => span?.SetTag("payload.bytes", rawFeaturesJson.Length)
                                  .SetTag("map.div_id",   divId),
            after: (_, elapsedMs) =>
            {
                ImportDiagnostics.RenderRawJsonDurationMs.Record(elapsedMs);
                Logger.LogInformation(
                    "MapInterop.RenderAllFeaturesRawJsonAsync — {PayloadBytes} B in {ElapsedMs} ms",
                    rawFeaturesJson.Length, elapsedMs);
            });

    /// <remarks>Tags carry counts and the state only — never feature ids (unbounded cardinality).</remarks>
    public Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        if (featureIds.Count == 0) return selectionInner.SetFeatureStatesAsync(divId, featureIds, state);

        return TrackAsync(
            SetFeatureStatesSpanName,
            () => selectionInner.SetFeatureStatesAsync(divId, featureIds, state),
            before: span => span?.SetTag("map.feature_count", featureIds.Count)
                                  .SetTag("map.feature_state", state.ToJsName())
                                  .SetTag("map.render_mode",   _renderModeTag));
    }

    /// <remarks>Arming a tool is a user action worth a span of its own: it is the start of every
    /// selection gesture, and a slow or failing JS call here is what the user perceives as "the
    /// tool did not arm".</remarks>
    public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) =>
        TrackAsync(
            SetSelectionToolSpanName,
            () => selectionInner.SetSelectionToolAsync(divId, tool),
            before: span => span?.SetTag("map.selection_tool", tool.ToJsName())
                                  .SetTag("map.render_mode",    _renderModeTag));

    /// <remarks>A range decides whether a whole type is on the map, so "the layer vanished" has to
    /// be traceable to the call that set it. Tags carry the bounds (absent when open) and the
    /// render mode — never the asset type id.</remarks>
    public Task SetLayerZoomRangeAsync(string divId, string assetTypeId, int? minZoom, int? maxZoom) =>
        TrackAsync(
            SetLayerZoomRangeSpanName,
            () => inner.SetLayerZoomRangeAsync(divId, assetTypeId, minZoom, maxZoom),
            before: span =>
            {
                if (minZoom is { } min) span?.SetTag("map.min_zoom", min);
                if (maxZoom is { } max) span?.SetTag("map.max_zoom", max);
                span?.SetTag("map.render_mode", _renderModeTag);
            });

    /// <remarks>Tags carry the ring and vertex counts only — never coordinates.</remarks>
    public Task ShowSelectionOutlineAsync(string divId, double[][][] rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        return TrackAsync(
            ShowSelectionOutlineSpanName,
            () => selectionInner.ShowSelectionOutlineAsync(divId, rings),
            before: span => span?.SetTag("map.outline_ring_count",   rings.Length)
                                  .SetTag("map.outline_vertex_count", rings.Sum(r => r?.Length ?? 0))
                                  .SetTag("map.render_mode",          _renderModeTag));
    }

    /// <remarks>Another organization's layer is redrawn on every settled pan, so a slow draw has to be traceable.
    /// Tags carry the payload size, the label zoom and the render mode — never the layer id (it names the
    /// organization) or anything from the payload.</remarks>
    public Task SetReferenceLayerAsync(string divId, string layerId, string rawFeaturesJson, ReferenceLayerOptions options)
    {
        ArgumentNullException.ThrowIfNull(rawFeaturesJson);
        ArgumentNullException.ThrowIfNull(options);
        return TrackAsync(
            SetReferenceLayerSpanName,
            () => inner.SetReferenceLayerAsync(divId, layerId, rawFeaturesJson, options),
            before: span => span?.SetTag("payload.bytes",       rawFeaturesJson.Length)
                                  .SetTag("map.label_min_zoom", options.LabelMinZoom)
                                  .SetTag("map.render_mode",    _renderModeTag));
    }

    // ── Pass-through ─────────────────────────────────────────────────────────

    public Task ClearReferenceLayerAsync(string divId, string layerId)                      => inner.ClearReferenceLayerAsync(divId, layerId);
    public Task<BoundingBox?> GetViewBoundsAsync(string divId)                               => inner.GetViewBoundsAsync(divId);

    public Task ClearFeatureStatesAsync(string divId)                                       => selectionInner.ClearFeatureStatesAsync(divId);
    public Task ClearSelectionOutlineAsync(string divId)                                    => selectionInner.ClearSelectionOutlineAsync(divId);
    public Task InitializeMapAsync(string divId, double lat, double lon, int zoom)           => inner.InitializeMapAsync(divId, lat, lon, zoom);
    public Task DestroyMapAsync(string divId)                                               => inner.DestroyMapAsync(divId);
    public Task InvalidateSizeAsync(string divId)                                            => inner.InvalidateSizeAsync(divId);
    public Task SetViewAsync(string divId, double lat, double lon, int zoom)                => inner.SetViewAsync(divId, lat, lon, zoom);
    public Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features)   => inner.RenderAllFeaturesAsync(divId, features);
    public Task RenderFeatureBatchRawJsonAsync(string divId, string rawFeaturesJson)         => inner.RenderFeatureBatchRawJsonAsync(divId, rawFeaturesJson);
    public Task RenderFeatureBatchAsync(string divId, IEnumerable<GeoFeature> features)      => inner.RenderFeatureBatchAsync(divId, features);
    public Task RenderFeatureBatchAsync(string divId, IReadOnlyList<JsonElement> features)   => inner.RenderFeatureBatchAsync(divId, features);
    public Task RemoveFeatureAsync(string divId, string featureId)                    => inner.RemoveFeatureAsync(divId, featureId);
    public Task ClearAllFeaturesAsync(string divId)                                   => inner.ClearAllFeaturesAsync(divId);
    public Task<int> BeginViewportRenderAsync(string divId)                           => inner.BeginViewportRenderAsync(divId);
    public Task EndViewportRenderAsync(string divId, int generation)                  => inner.EndViewportRenderAsync(divId, generation);
    public Task EnableDrawModeAsync(string divId, GeometryType mode)                  => inner.EnableDrawModeAsync(divId, mode);
    public Task DisableDrawModeAsync(string divId)                                    => inner.DisableDrawModeAsync(divId);
    public Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds) => inner.SetSnapTargetLayerAsync(divId, allowedTargetAssetTypeIds);
    public Task ClearSnapTargetScopeAsync(string divId)                               => inner.ClearSnapTargetScopeAsync(divId);
    public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) => inner.AddTileLayerAsync(divId, layerId, url, options);
    public Task RemoveTileLayerAsync(string divId, string layerId)                                            => inner.RemoveTileLayerAsync(divId, layerId);
    public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options)    => inner.AddWmsLayerAsync(divId, layerId, wmsBaseUrl, options);
    public Task RemoveWmsLayerAsync(string divId, string layerId)                                             => inner.RemoveWmsLayerAsync(divId, layerId);
    public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible)                      => inner.SetLayerVisibilityAsync(divId, assetTypeId, visible);
    public Task FitBoundsAsync(string divId, double[] bbox)                           => inner.FitBoundsAsync(divId, bbox);
    public Task PanToFeatureAsync(string divId, string featureId)                     => inner.PanToFeatureAsync(divId, featureId);
    public Task HighlightFeatureAsync(string divId, string featureId)                 => inner.HighlightFeatureAsync(divId, featureId);
    public Task ClearHighlightAsync(string divId, string featureId)                   => inner.ClearHighlightAsync(divId, featureId);
    public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) => inner.RegisterEventHandlersAsync(divId, handlerRef);
}
