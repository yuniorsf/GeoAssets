using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
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

    // --- Feature rendering ---
    Task RenderFeatureAsync(string divId, GeoFeature feature);
    Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features);
    Task RemoveFeatureAsync(string divId, string featureId);
    Task ClearAllFeaturesAsync(string divId);

    // --- Draw tools ---
    Task EnableDrawModeAsync(string divId, GeometryType mode);
    Task DisableDrawModeAsync(string divId);

    // --- Layer visibility ---
    Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible);

    // --- View ---
    Task FitBoundsAsync(string divId, double[] bbox);
    Task PanToFeatureAsync(string divId, string featureId);

    // --- Events raised back to .NET ---
    Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef);
}
