namespace GeoAssets.Shared.Services;

/// <summary>
/// Selects the rendering backend used to draw geo-features on the map.
/// Configure via <c>appsettings.json → "MapInterop:RenderMode"</c>.
/// </summary>
public enum MapRenderMode
{
    /// <summary>Default SVG rendering via Leaflet's L.geoJSON (best compatibility).</summary>
    Leaflet,

    /// <summary>
    /// Leaflet's built-in Canvas renderer — same API as Leaflet but renders to a
    /// &lt;canvas&gt; element instead of SVG.  Better performance for large feature counts
    /// because it avoids DOM node overhead.
    /// </summary>
    Canvas,

    /// <summary>
    /// Custom WebGL overlay.  Features are rasterised on a WebGL &lt;canvas&gt;
    /// positioned over the map; click / context-menu events are captured by thin,
    /// invisible Leaflet SVG layers.  Highest throughput for very large datasets.
    /// <para>
    /// Limitations: polygon fill uses ear-clipping triangulation of the exterior ring only
    /// (holes are outlined but not cut out).
    /// </para>
    /// </summary>
    WebGL
}

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
    public bool SinglePass { get; set; } = false;

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
}
