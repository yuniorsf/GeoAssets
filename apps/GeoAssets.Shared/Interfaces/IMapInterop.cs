using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Microsoft.JSInterop;

namespace GeoAssets.Shared.Interfaces;

/// <summary>Options for a Leaflet <c>L.tileLayer</c> or WMS tile layer.</summary>
/// <param name="Attribution">HTML attribution text shown in the map corner.</param>
/// <param name="MaxZoom">Maximum native zoom level for this layer.</param>
/// <param name="MinZoom">Minimum native zoom level for this layer.</param>
/// <param name="Opacity">Layer opacity in [0, 1].</param>
public record TileLayerOptions(
    string Attribution = "",
    int    MaxZoom     = 19,
    int    MinZoom     = 0,
    double Opacity     = 1.0);

/// <summary>
/// Abstraction over IJSRuntime calls to the Leaflet map instance.
/// Every method corresponds to a function in mapInterop.js / drawInterop.js.
/// </summary>
public interface IMapInterop
{
    // --- Lifecycle ---
    Task InitializeMapAsync(string divId, double lat, double lon, int zoom);
    Task DestroyMapAsync(string divId);

    // --- Feature rendering ---
    Task RenderFeatureAsync(string divId, GeoFeature feature);
    Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features);
    /// <summary>Renders features from pre-serialized <see cref="JsonElement"/> objects, avoiding a re-serialize step.</summary>
    Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features);
    Task RemoveFeatureAsync(string divId, string featureId);
    Task ClearAllFeaturesAsync(string divId);

    // --- Draw tools ---
    Task EnableDrawModeAsync(string divId, GeometryType mode);
    Task DisableDrawModeAsync(string divId);

    // --- Tile / WMS layers ---
    Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null);
    Task RemoveTileLayerAsync(string divId, string layerId);

    // --- Layer visibility ---
    Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible);

    // --- View ---
    Task FitBoundsAsync(string divId, double[] bbox);
    Task PanToFeatureAsync(string divId, string featureId);

    // --- Events raised back to .NET ---
    Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef);
}
