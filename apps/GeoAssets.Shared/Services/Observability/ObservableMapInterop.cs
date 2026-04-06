using System.Text.Json;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Services.Observability;

/// <summary>
/// Observable decorator for <see cref="IMapInterop"/>.
/// Instruments <see cref="RenderAllFeaturesAsync"/> with an OpenTelemetry span,
/// a duration metric, and structured logging; all other calls are
/// forwarded to the inner service without overhead.
/// </summary>
public sealed class ObservableMapInterop(
    IMapInterop inner,
    ILogger<ObservableMapInterop> logger)
    : ObservableDecoratorBase<ObservableMapInterop>(logger), IMapInterop
{
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

    // ── Pass-through ─────────────────────────────────────────────────────────

    public Task InitializeMapAsync(string divId, double lat, double lon, int zoom)           => inner.InitializeMapAsync(divId, lat, lon, zoom);
    public Task DestroyMapAsync(string divId)                                               => inner.DestroyMapAsync(divId);
    public Task RenderFeatureAsync(string divId, GeoFeature feature)                        => inner.RenderFeatureAsync(divId, feature);
    public Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features)   => inner.RenderAllFeaturesAsync(divId, features);
    public Task RemoveFeatureAsync(string divId, string featureId)                    => inner.RemoveFeatureAsync(divId, featureId);
    public Task ClearAllFeaturesAsync(string divId)                                   => inner.ClearAllFeaturesAsync(divId);
    public Task EnableDrawModeAsync(string divId, GeometryType mode)                  => inner.EnableDrawModeAsync(divId, mode);
    public Task DisableDrawModeAsync(string divId)                                    => inner.DisableDrawModeAsync(divId);
    public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) => inner.AddTileLayerAsync(divId, layerId, url, options);
    public Task RemoveTileLayerAsync(string divId, string layerId)                                            => inner.RemoveTileLayerAsync(divId, layerId);
    public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible)                      => inner.SetLayerVisibilityAsync(divId, assetTypeId, visible);
    public Task FitBoundsAsync(string divId, double[] bbox)                           => inner.FitBoundsAsync(divId, bbox);
    public Task PanToFeatureAsync(string divId, string featureId)                     => inner.PanToFeatureAsync(divId, featureId);
    public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) => inner.RegisterEventHandlersAsync(divId, handlerRef);
}
