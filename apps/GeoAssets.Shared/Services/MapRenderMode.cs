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
