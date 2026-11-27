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
    /// Polygon fill uses ear-clipping triangulation; interior rings (holes) are cut out of the
    /// fill, as in the other modes.  A hole that lies outside its exterior ring is ignored, and
    /// one that crosses it (invalid geometry) is not cut exactly.
    /// </para>
    /// </summary>
    WebGL,

    /// <summary>
    /// MapLibre GL JS (XD01-435, ADR-002 D6) — <c>geoassets.maplibre.js</c>, loaded lazily (with MapLibre GL JS and
    /// terra-draw from the CDN) only when this mode is configured, behind the same <see cref="Interfaces.IMapInterop"/>
    /// contract and the same <c>MapContainer</c> callbacks. Draws the kernel's <c>/tiles/style.json</c> (vector and raster
    /// tiles, bearer-authenticated through MapLibre's <c>transformRequest</c>, re-read after a write and every
    /// <see cref="MapLibreOptions.StyleRefreshInterval"/> to follow <c>{v}</c>) under the GeoJSON features the providers
    /// render; click, hover and context menu are GPU feature queries; draw, edit and snap are terra-draw's. What it can't
    /// do is documented on <see cref="MapLibreOptions"/>. Web only for now: MAUI's Content-Security-Policy blocks the
    /// gateway fetches and blob workers it needs, so MAUI stays on <see cref="Leaflet"/>.
    /// </summary>
    MapLibre
}
