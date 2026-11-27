namespace GeoAssets.Shared.Services;

/// <summary>
/// Controls how <see cref="MapInteropService"/> renders features to the Leaflet map.
/// Configure via <c>builder.Services.Configure&lt;MapInteropOptions&gt;(…)</c> or
/// <c>appsettings.json → "MapInterop"</c>.
/// </summary>
public sealed class MapInteropOptions
{
    /// <summary>
    /// When <c>true</c>, all features are sent to JS in a single batch call.
    /// When <c>false</c> (default), features are sent in chunks of <see cref="BatchSize"/>,
    /// yielding to the browser event loop between chunks.
    /// </summary>
    public bool SinglePass { get; set; }

    /// <summary>
    /// Number of features per batch when <see cref="SinglePass"/> is <c>false</c>.
    /// Defaults to 5. Ignored when <see cref="SinglePass"/> is <c>true</c>.
    /// </summary>
    public int BatchSize { get; set; } = 5;

    /// <summary>
    /// Rendering backend used to draw features on the map.
    /// Defaults to <see cref="MapRenderMode.Leaflet"/> (SVG).
    /// </summary>
    public MapRenderMode RenderMode { get; set; } = MapRenderMode.Leaflet;

    /// <summary>The <see cref="MapRenderMode.MapLibre"/> settings (XD01-435); the Leaflet modes read only its
    /// <see cref="MapLibreOptions.StyleUrl"/>, <see cref="MapLibreOptions.StyleRefreshInterval"/> and
    /// <see cref="MapLibreOptions.WriteRefreshDelay"/>, and only with <see cref="LeafletTileSplit"/>.</summary>
    public MapLibreOptions MapLibre { get; set; } = new();

    /// <summary>
    /// The interim Leaflet split (XD01-436) in the Leaflet modes (<see cref="MapRenderMode.Leaflet"/>,
    /// <see cref="MapRenderMode.Canvas"/>, <see cref="MapRenderMode.WebGL"/>): the kernel's provider entry is drawn by
    /// <see cref="Rendering.HybridLodMapLayerRenderer"/> — the style's raster tiles at the zooms of its raster tier, the
    /// discrete features at every other zoom; a style without a raster tier keeps the features at every zoom, as before.
    /// Off by default, so a host that doesn't turn it on (MAUI) draws exactly as before.
    /// </summary>
    public bool LeafletTileSplit { get; set; }
}
