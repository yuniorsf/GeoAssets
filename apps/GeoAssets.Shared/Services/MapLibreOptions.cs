using GeoAssets.Identity.Authentication;

namespace GeoAssets.Shared.Services;

/// <summary>
/// <see cref="MapRenderMode.MapLibre"/>'s settings (XD01-435), <c>appsettings.json → "MapInterop:MapLibre"</c>.
/// <para>
/// <b>What the MapLibre mode does not do as the Leaflet modes do</b> — each degrades explicitly (a one-time
/// <c>console.warn</c> naming the call) and never throws:
/// <list type="bullet">
///   <item><c>AddWmsLayerAsync</c> requests EPSG:3857 tiles (<c>{bbox-epsg-3857}</c>), the only grid MapLibre draws; a WMS
///   layer of this app's own server (under <see cref="ApiScopeRoutes.BaseUrl"/>), which answers EPSG:4326 only, is not
///   drawn — the kernel's features come from <c>/tiles/style.json</c> instead.</item>
///   <item>A resolved style's <c>IconUrl</c> is drawn as the circle of its color (no sprite or image is loaded).</item>
///   <item>Feature states and the highlight change color, opacity and width only, as in WebGL mode.</item>
///   <item>Reference layers draw no labels (the style has no glyphs).</item>
///   <item>Editing is terra-draw's select mode, started by a double-click on a feature the providers rendered; a
///   double-click on a tile-only one selects it and edits its GeoJSON overlay once that arrives (XD01-436) — never the
///   tile's clipped geometry. A multi-geometry is not editable.</item>
///   <item>An offline vector map pack (<see cref="Core.Interfaces.ILocalVectorTileProvider"/>, XD01-460) is drawn from its
///   tiles, read on the device through the <c>geoassets-pack://</c> protocol: its features are shown, not clicked, hovered,
///   selected or snapped to. An offline raster pack is a basemap under every other layer, as in the Leaflet modes.</item>
///   <item>On a touch screen a feature's context menu opens on a long press only where the platform fires
///   <c>contextmenu</c> for one (Android) — unless <see cref="LongPressContextMenu"/> is on (MAUI turns it on).</item>
/// </list>
/// The kernel's own provider entry is drawn by <see cref="Rendering.HybridLodMapLayerRenderer"/> (XD01-436): from its
/// tiles, with only the selected and just-written features as GeoJSON over tile copies hidden by id — no feature is
/// drawn twice. Another provider's features are drawn over the tiles as before.
/// Zooms cross the contract in Leaflet's 256 px scale (MapLibre's zoom + 1), so the initial zoom, <c>SetViewAsync</c>,
/// zoom ranges and the zoom reported to <c>OnViewportChangedFromJs</c> mean the same ground resolution in every mode.
/// </para>
/// </summary>
public sealed class MapLibreOptions
{
    /// <summary>The kernel style path under <see cref="ApiScopeRoutes.BaseUrl"/> (the gateway).</summary>
    public const string DefaultStyleUrl = "tiles/style.json";

    /// <summary>
    /// The MapLibre style of the kernel's tiles. Relative (the default, <see cref="DefaultStyleUrl"/>) = under the
    /// gateway's <c>GeoAssetsServer:BaseUrl</c>; an absolute http(s) URL is used as it is. Every request under this URL's
    /// directory (the style and its tiles) carries the bearer token <c>MapContainer.GetTileAccessFromJs</c> answers for it.
    /// </summary>
    public string StyleUrl { get; set; } = DefaultStyleUrl;

    /// <summary>
    /// How often an open map re-reads <see cref="StyleUrl"/> to follow the tiles' <c>{v}</c> (another user's writes);
    /// <see cref="TimeSpan.Zero"/> = only after this client's own writes. Defaults to 1 minute.
    /// </summary>
    public TimeSpan StyleRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long after this client's last write (a feature added, updated or deleted, or the collection changed) the style
    /// is re-read — writes within this delay are one re-read, which lands after the server committed them. Defaults to
    /// 1.5 seconds.
    /// </summary>
    public TimeSpan WriteRefreshDelay { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// <see cref="StyleUrl"/> made absolute against <paramref name="gatewayBaseUrl"/> unless it is an absolute http(s) URL
    /// already; left as it is (for the page's base URI to resolve) when there is no gateway. A blank value is
    /// <see cref="DefaultStyleUrl"/>.
    /// </summary>
    public static string ResolveStyleUrl(string? styleUrl, Uri? gatewayBaseUrl)
    {
        var url = string.IsNullOrWhiteSpace(styleUrl) ? DefaultStyleUrl : styleUrl.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return absolute.AbsoluteUri;

        if (gatewayBaseUrl is null)
            return url;

        // "./" keeps anything that parses as another scheme (javascript:, file:, //host) a path under the gateway.
        var root = gatewayBaseUrl.AbsoluteUri.EndsWith('/') ? gatewayBaseUrl : new Uri(gatewayBaseUrl.AbsoluteUri + "/");
        return new Uri(root, "./" + url.TrimStart('/')).AbsoluteUri;
    }

    /// <summary>
    /// Where MapLibre GL JS (its CSP build, <c>maplibre-gl-csp.js</c> with its worker), its stylesheet, terra-draw and its
    /// adapter are loaded from (XD01-460): a URL (relative = under the page's base URI) under which each sits at its
    /// unpkg path, e.g. <c>maplibre-gl@5.24.0/dist/maplibre-gl-csp.js</c> — the same pinned versions and SRI hashes as
    /// the CDN's. <c>null</c> (the default, the Web) = the pinned unpkg URLs. MAUI points it at the copies it bundles, so
    /// the map starts offline and the page needs no script origin.
    /// </summary>
    public string? LibraryBaseUrl { get; set; }

    /// <summary>
    /// Whether a long press (500 ms, a finger held within 10 px) on a feature opens its context menu, as a right-click
    /// does (XD01-460) — for WebViews that fire no <c>contextmenu</c> for one (iOS, Mac Catalyst); the platform's own is
    /// used where it fires one, never both. Off by default (the Web); MAUI turns it on.
    /// </summary>
    public bool LongPressContextMenu { get; set; }

    /// <summary>Throws when an interval is negative.</summary>
    public static void EnsureValid(MapLibreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.StyleRefreshInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.StyleRefreshInterval,
                "MapInterop:MapLibre:StyleRefreshInterval must not be negative.");
        if (options.WriteRefreshDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.WriteRefreshDelay,
                "MapInterop:MapLibre:WriteRefreshDelay must not be negative.");
    }
}
