using System.Text.RegularExpressions;

namespace GeoAssets.MAUI.Services.Map;

/// <summary>
/// The host page (<c>wwwroot/index.html</c>) in <c>MapInterop:RenderMode = MapLibre</c> (XD01-460): the same page with its
/// Content-Security-Policy widened by exactly what that mode needs, and nothing in the Leaflet modes, whose page is
/// served as it is. The two additions:
/// <list type="bullet">
///   <item><c>worker-src blob:</c> — MapLibre's worker runs from a <c>blob:</c> URL of the bundled, SRI-checked
///   <c>maplibre-gl-csp-worker.js</c>; without the directive a worker falls back to <c>script-src</c>, which has no
///   <c>blob:</c>.</item>
///   <item>the tile style's origin (the gateway, <c>GeoAssetsServer:BaseUrl</c>) in <c>connect-src</c> — the page
///   fetches <c>/tiles/style.json</c> and MapLibre its vector and raster tiles with <c>fetch</c> (a bearer header can't
///   ride on an <c>&lt;img&gt;</c>), so neither <c>img-src</c> nor <c>script-src</c> gains an origin.</item>
/// </list>
/// The scripts are the app's own copies (<c>'self'</c>), the offline packs' tiles come through the
/// <c>geoassets-pack://</c> protocol (no request), and OpenStreetMap's tiles are already in <c>connect-src</c>.
/// </summary>
internal static partial class MapLibreHostPage
{
    private static readonly char[] Whitespace = [' ', '\t', '\r', '\n'];

    /// <summary>
    /// <paramref name="hostPage"/> with <c>worker-src blob:</c> and <paramref name="tileOrigin"/>'s origin added to its
    /// Content-Security-Policy meta tag, each only when not there already; every other directive is kept as it is.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="tileOrigin"/> is not an absolute http(s) URL.</exception>
    /// <exception cref="InvalidOperationException">The page has no Content-Security-Policy meta tag with a <c>connect-src</c>.</exception>
    public static string WidenContentSecurityPolicy(string hostPage, Uri tileOrigin)
    {
        ArgumentNullException.ThrowIfNull(hostPage);
        var origin = OriginOf(tileOrigin);

        var meta = CspMeta().Match(hostPage);
        if (!meta.Success)
            throw new InvalidOperationException("The host page has no Content-Security-Policy meta tag to widen for the MapLibre render mode.");

        var directives = meta.Groups["policy"].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries).ToList())
            .ToList();
        var connect = directives.Find(d => string.Equals(d[0], "connect-src", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The host page's Content-Security-Policy has no connect-src to add the tile style's origin to.");
        if (!connect.Skip(1).Contains(origin, StringComparer.OrdinalIgnoreCase))
            connect.Add(origin);

        var worker = directives.Find(d => string.Equals(d[0], "worker-src", StringComparison.OrdinalIgnoreCase));
        if (worker is null)
            directives.Add(["worker-src", "blob:"]);
        else if (!worker.Skip(1).Contains("blob:", StringComparer.OrdinalIgnoreCase))
            worker.Add("blob:");

        var policy = string.Join("; ", directives.Select(d => string.Join(' ', d))) + ";";
        var group = meta.Groups["policy"];
        return string.Concat(hostPage.AsSpan(0, group.Index), policy, hostPage.AsSpan(group.Index + group.Length));
    }

    /// <summary>The CSP source of <paramref name="url"/>'s origin: <c>scheme://host[:port]</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute http(s) URL.</exception>
    public static string OriginOf(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
            throw new ArgumentException($"The tile style's origin must be an absolute http(s) URL, not '{url}'.", nameof(url));
        return url.GetLeftPart(UriPartial.Authority);
    }

    [GeneratedRegex("""<meta\s+http-equiv="Content-Security-Policy"\s+content="(?<policy>[^"]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CspMeta();
}
