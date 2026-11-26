using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Abstraction over IJSRuntime calls to the Leaflet map instance.
/// Every method corresponds to a function in mapInterop.js / drawInterop.js.
/// </summary>
public interface IMapInterop
{
    // --- Lifecycle ---
    Task InitializeMapAsync(string divId, double lat, double lon, int zoom);
    Task DestroyMapAsync(string divId);
    Task InvalidateSizeAsync(string divId);

    /// <summary>Re-centers/re-zooms an already-initialized map — unlike <see cref="InitializeMapAsync"/>
    /// (idempotent, a no-op once the map exists), this always applies.</summary>
    Task SetViewAsync(string divId, double lat, double lon, int zoom);

    // --- Feature rendering ---
    Task RenderFeatureAsync(string divId, GeoFeature feature);
    Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features);
    /// <summary>Renders features from pre-serialized <see cref="JsonElement"/> objects, avoiding a re-serialize step.</summary>
    Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features);
    /// <summary>
    /// Renders features from a raw JSON array string, bypassing all C# JSON parsing.
    /// The string is forwarded directly to JavaScript which parses it natively.
    /// </summary>
    Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson);
    /// <summary>
    /// Renders a raw JSON array of features additively — unlike <see cref="RenderAllFeaturesRawJsonAsync"/>,
    /// this does <b>not</b> clear existing features first. Intended for progressive rendering of
    /// several chunks in sequence (e.g. <see cref="GeoAssets.Core.Interfaces.IAssetProvider.GetInBoundsRawJsonChunksAsync"/>) —
    /// callers clear once via <see cref="ClearAllFeaturesAsync"/> before the first chunk.
    /// </summary>
    Task RenderFeatureBatchRawJsonAsync(string divId, string rawFeaturesJson);
    /// <summary>
    /// Renders a batch of features additively — unlike <see cref="RenderAllFeaturesAsync(string, IEnumerable{GeoFeature})"/>,
    /// this does <b>not</b> clear existing features first. Unlike <see cref="RenderFeatureBatchRawJsonAsync"/>,
    /// it still resolves each feature's Layer/LayerRule styling (<c>GeoFeature</c> objects, not raw JSON).
    /// Intended for streaming a large dataset in chunks (e.g. <see cref="GeoAssets.Core.Services.FeatureRenderPipeline.StreamAllAsync"/>) —
    /// callers clear once via <see cref="ClearAllFeaturesAsync"/> before the first chunk.
    /// </summary>
    Task RenderFeatureBatchAsync(string divId, IEnumerable<GeoFeature> features);
    /// <summary>
    /// Renders features from pre-serialized <see cref="JsonElement"/> objects additively — the
    /// <see cref="JsonElement"/> counterpart to <see cref="RenderFeatureBatchAsync(string, IEnumerable{GeoFeature})"/>,
    /// as <see cref="RenderFeatureBatchRawJsonAsync"/> is to <see cref="RenderAllFeaturesRawJsonAsync"/>:
    /// same payload shape as <see cref="RenderAllFeaturesAsync(string, IReadOnlyList{JsonElement})"/>, but
    /// does not clear existing features first, so callers rendering several providers' results onto
    /// the same map don't erase each other.
    /// </summary>
    Task RenderFeatureBatchAsync(string divId, IReadOnlyList<JsonElement> features);
    Task RemoveFeatureAsync(string divId, string featureId);
    Task ClearAllFeaturesAsync(string divId);

    /// <summary>
    /// Opens a viewport pass (XD01-429) — a viewport refresh that diffs instead of clearing the map: every feature
    /// rendered while the pass is open is kept (one already drawn with the same data and style keeps its layer), and
    /// <see cref="EndViewportRenderAsync"/> removes only those it did not render. Opening a pass, or
    /// <see cref="ClearAllFeaturesAsync"/>, supersedes the open one: a batch of it still being drawn stops, and its end
    /// removes nothing. Answers the pass's generation, for <see cref="EndViewportRenderAsync"/>.
    /// </summary>
    Task<int> BeginViewportRenderAsync(string divId);

    /// <summary>
    /// Closes the viewport pass <paramref name="generation"/> opened by <see cref="BeginViewportRenderAsync"/>, removing
    /// every feature it did not render. A no-op when that pass was superseded. Reference layers, WMS/tile layers and the
    /// per-feature states are untouched.
    /// </summary>
    Task EndViewportRenderAsync(string divId, int generation);

    // --- Draw tools ---
    Task EnableDrawModeAsync(string divId, GeometryType mode);
    Task DisableDrawModeAsync(string divId);

    /// <summary>
    /// Scopes Geoman snap targets to only the layers of <paramref name="allowedTargetAssetTypeIds"/>
    /// (an empty collection is a valid "nothing to snap to" scope). Call before
    /// <see cref="EnableDrawModeAsync"/> so the scope applies to the session about to start; see
    /// <see cref="ClearSnapTargetScopeAsync"/> to restore unscoped/global snapping.
    /// </summary>
    Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds);

    /// <summary>Restores unscoped/global snapping — every rendered layer is a valid snap target again.</summary>
    Task ClearSnapTargetScopeAsync(string divId);

    // --- Tile / WMS layers ---
    Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null);
    Task RemoveTileLayerAsync(string divId, string layerId);

    // --- OGC WMS layers ---
    Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options);
    Task RemoveWmsLayerAsync(string divId, string layerId);

    // --- Layer visibility ---
    Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible);

    /// <summary>
    /// Sets the zoom range in which features of <paramref name="assetTypeId"/> are drawn
    /// (XD01-311): the type shows only while it is user-visible (<see cref="SetLayerVisibilityAsync"/>)
    /// and <paramref name="minZoom"/> ≤ map zoom ≤ <paramref name="maxZoom"/>. A <c>null</c> bound
    /// is open; two nulls remove the range. The two rules are independent and compose. A range
    /// that breaks <see cref="GeoAssets.Core.Models.AssetTypeZoomRange"/>'s rules throws
    /// <see cref="GeoAssets.Core.Models.InvalidAssetTypeZoomRangeException"/> and makes no JS call.
    /// Client-side only: the provider still returns features of every type for the viewport.
    /// </summary>
    Task SetLayerZoomRangeAsync(string divId, string assetTypeId, int? minZoom, int? maxZoom);

    // --- Reference layers of another organization (XD01-331) ---

    /// <summary>
    /// Draws <paramref name="rawFeaturesJson"/> — a JSON array of <see cref="ReferenceFeature"/>s as
    /// <c>GET …/organizations/{id}/reference-features</c> answers it, forwarded without parsing — as the reference layer
    /// <paramref name="layerId"/>, replacing whatever that layer showed. Reference layers live in their own pane below the
    /// user's features, in a muted theme style, each feature labelled from <see cref="ReferenceLayerOptions.LabelMinZoom"/>
    /// up. They are not features: fully non-interactive (no click, context menu, popup or tooltip on hover), never
    /// editable or snappable by Leaflet-Geoman, never part of the draw or selection tools, and untouched by
    /// <see cref="ClearAllFeaturesAsync"/>, layer visibility, zoom ranges and feature states.
    /// </summary>
    Task SetReferenceLayerAsync(string divId, string layerId, string rawFeaturesJson, ReferenceLayerOptions options);

    /// <summary>Removes the reference layer <paramref name="layerId"/>; a no-op when it is not drawn.</summary>
    Task ClearReferenceLayerAsync(string divId, string layerId);

    /// <summary>The map's current view as Leaflet reports it (west, south, east, north); null when the map does not exist.</summary>
    Task<BoundingBox?> GetViewBoundsAsync(string divId);

    // --- View ---
    Task FitBoundsAsync(string divId, double[] bbox);
    Task PanToFeatureAsync(string divId, string featureId);

    /// <summary>
    /// Applies a temporary highlight style to one already-rendered feature's layer (used by the
    /// multi-candidate "Connect to…" picker, XD01-152, to show which candidate is currently
    /// selected while cycling). A no-op in WebGL render mode — per-feature dynamic restyling
    /// isn't supported by that renderer.
    /// </summary>
    Task HighlightFeatureAsync(string divId, string featureId);

    /// <summary>Restores the style <see cref="HighlightFeatureAsync"/> overrode.</summary>
    Task ClearHighlightAsync(string divId, string featureId);

    // --- Events raised back to .NET ---
    Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef);
}
