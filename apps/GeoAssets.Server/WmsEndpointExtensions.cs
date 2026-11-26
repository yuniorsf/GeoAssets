using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Rendering;

namespace GeoAssets.Server;

/// <summary>
/// Maps an OGC WMS 1.1.1 KVP endpoint onto any <see cref="IEndpointRouteBuilder"/>.
///
/// All data access goes through the request's <see cref="IAssetRenderQuery"/> (XD01-372) — the provider's pushdown when
/// it registers one (PostgreSQL: one <c>ST_Intersects</c> query per tile projecting geometry and style inputs only), the
/// generic query over the request's <see cref="IAssetProvider"/> otherwise — and the request's catalog for the
/// capabilities' layers. Both are scoped to the caller's organization: its features plus the shared ones.
///
/// Supported operations:
/// <list type="bullet">
///   <item><term>GetCapabilities</term><description>WMS 1.1.1 service metadata XML.</description></item>
///   <item><term>GetMap</term><description>PNG raster image drawn by <see cref="SkiaFeaturePainter"/> (XD01-433) from the render query's features —
///   those whose asset type is drawn at the zoom of the request's scale (<see cref="ZoomOf"/>), at most
///   <see cref="RenderQueryOptions.DefaultMaxCount"/> (XD01-430).</description></item>
///   <item><term>GetFeatureInfo</term><description>JSON properties for features at a clicked pixel.</description></item>
/// </list>
///
/// All operations here are read-only, so the whole endpoint requires only
/// <c>features:read</c> (XD01-15) — external GIS clients (QGIS etc.) authenticate the same
/// way any other caller does: a bearer token on the request.
/// </summary>
public static class WmsEndpointExtensions
{
    // Minimal valid 1×1 transparent PNG returned when the client cancels a tile request.
    // Pre-computed so we never spin up SkiaSharp for a cancelled request.
    private static readonly byte[] _emptyTilePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // One instance for every GetFeatureInfo answer (XD01-430), not one per request.
    private static readonly JsonSerializerOptions _featureInfoJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Maps the endpoint at <paramref name="route"/>. Every operation requires <c>features:read</c>; nothing opens it
    /// (XD01-96 replaced the interim switch XD01-95 added to serve it anonymously). The in-app map fetches its tiles with
    /// the caller's bearer token. Every response is marked <c>Cache-Control: private</c>: it shows the caller's
    /// organization's features, so no shared cache may store it.
    /// </summary>
    public static IEndpointRouteBuilder MapWmsApi(
        this IEndpointRouteBuilder routes,
        string route = "/wms")
    {
        routes.MapGet(route, async (HttpRequest req, IAssetRenderQuery renderer, IAssetProvider catalog, CancellationToken ct) =>
        {
            req.HttpContext.Response.Headers.CacheControl = "private";

            string Param(string key)
            {
                foreach (var k in new[] { key, key.ToUpperInvariant(), key.ToLowerInvariant() })
                    if (req.Query.TryGetValue(k, out var v)) return v.ToString() ?? string.Empty;
                return string.Empty;
            }

            try
            {
                return Param("REQUEST").ToUpperInvariant() switch
                {
                    "GETCAPABILITIES" => HandleGetCapabilities(req, catalog),
                    "GETMAP"          => await HandleGetMapAsync(Param, renderer, ct).ConfigureAwait(false),
                    "GETFEATUREINFO"  => await HandleGetFeatureInfoAsync(Param, renderer, ct).ConfigureAwait(false),
                    var op            => Results.BadRequest(
                        $"Unsupported WMS REQUEST: '{op}'. " +
                        "Supported: GetCapabilities, GetMap, GetFeatureInfo.")
                };
            }
            catch (OperationCanceledException)
            {
                // Leaflet cancelled the tile request (panned/zoomed away).
                // Return a transparent 1×1 PNG so the browser doesn't log a network error.
                return Results.Bytes(_emptyTilePng, "image/png");
            }
        }).RequireAuthorization("features:read");

        return routes;
    }

    // ── GetCapabilities ───────────────────────────────────────────────────────

    private static IResult HandleGetCapabilities(HttpRequest req, IAssetProvider catalog)
    {
        var baseUrl    = $"{req.Scheme}://{req.Host}{req.PathBase}{req.Path}";
        var assetTypes = catalog.GetAssetTypes();
        return Results.Content(BuildCapabilitiesXml(baseUrl, assetTypes), "application/xml; charset=utf-8");
    }

    private static string BuildCapabilitiesXml(
        string wmsUrl, IReadOnlyList<AssetType> assetTypes)
    {
        var subLayers = assetTypes.Select(t =>
            new XElement("Layer",
                new XElement("Name",  $"geoassets:feature:{t.Id}"),
                new XElement("Title", t.Name),
                new XElement("SRS",   "EPSG:4326"),
                BboxElement(-180, -90, 180, 90)));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("WMT_MS_Capabilities",
                new XAttribute("version", "1.1.1"),

                new XElement("Service",
                    new XElement("Name",    "OGC:WMS"),
                    new XElement("Title",   "GeoAssets WMS"),
                    new XElement("Abstract","OGC WMS 1.1.1"),
                    new XElement("OnlineResource",
                        new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3.org/1999/xlink"),
                        new XAttribute("{http://www.w3.org/1999/xlink}href", wmsUrl))),

                new XElement("Capability",
                    new XElement("Request",
                        WmsOp("GetCapabilities", wmsUrl, "application/xml"),
                        WmsOp("GetMap",          wmsUrl, "image/png"),
                        WmsOp("GetFeatureInfo",  wmsUrl, "application/json")),
                    new XElement("Exception",
                        new XElement("Format", "application/json")),
                    new XElement("Layer",
                        new XElement("Name",    "geoassets:feature"),
                        new XElement("Title",   "GeoAssets Features"),
                        new XElement("Abstract","All features stored in this GeoAssets instance"),
                        new XElement("SRS",     "EPSG:4326"),
                        BboxElement(-180, -90, 180, 90),
                        subLayers))));

        var sb = new StringBuilder();
        using var writer = System.Xml.XmlWriter.Create(sb,
            new System.Xml.XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 });
        doc.WriteTo(writer);
        writer.Flush();
        return sb.ToString();
    }

    private static XElement WmsOp(string name, string href, string format) =>
        new(name,
            new XElement("Format", format),
            new XElement("DCPType",
                new XElement("HTTP",
                    new XElement("Get",
                        new XElement("OnlineResource",
                            new XAttribute(XNamespace.Xmlns + "xlink", "http://www.w3.org/1999/xlink"),
                            new XAttribute("{http://www.w3.org/1999/xlink}href", href))))));

    private static XElement BboxElement(double minX, double minY, double maxX, double maxY) =>
        new("LatLonBoundingBox",
            new XAttribute("minx", minX.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("miny", minY.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("maxx", maxX.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("maxy", maxY.ToString(CultureInfo.InvariantCulture)));

    // ── GetMap ────────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleGetMapAsync(
        Func<string, string> param, IAssetRenderQuery renderer, CancellationToken ct)
    {
        if (!TryParseBbox(param("BBOX"), out var minLon, out var minLat, out var maxLon, out var maxLat))
            return Results.BadRequest("Invalid or missing BBOX. Expected: minLon,minLat,maxLon,maxLat");

        if (!int.TryParse(param("WIDTH"), CultureInfo.InvariantCulture,  out var width)  || width  <= 0 || width  > 4096)
            return Results.BadRequest("Invalid or missing WIDTH.");
        if (!int.TryParse(param("HEIGHT"), CultureInfo.InvariantCulture, out var height) || height <= 0 || height > 4096)
            return Results.BadRequest("Invalid or missing HEIGHT.");

        var transparent = param("TRANSPARENT").Equals("TRUE", StringComparison.OrdinalIgnoreCase);

        // Resolve optional asset-type filter from LAYERS=geoassets:feature:{typeId}
        var layerParam  = param("LAYERS");
        string? assetTypeId = layerParam.StartsWith("geoassets:feature:", StringComparison.OrdinalIgnoreCase)
            ? layerParam["geoassets:feature:".Length..]
            : null;

        var options = new RenderQueryOptions { AssetTypeId = assetTypeId, Zoom = ZoomOf(maxLon - minLon, width) };
        var bbox = new BoundingBox(minLon, minLat, maxLon, maxLat);
        var rows = await renderer.GetRenderableAsync(bbox, options, ct).ConfigureAwait(false);

        var pngBytes = SkiaFeaturePainter.RenderPng(rows, width, height, PixelTransform.LonLat(bbox, width, height), transparent);
        return Results.Bytes(pngBytes, "image/png");
    }

    // ── GetFeatureInfo ────────────────────────────────────────────────────────

    private static async Task<IResult> HandleGetFeatureInfoAsync(
        Func<string, string> param, IAssetRenderQuery renderer, CancellationToken ct)
    {
        if (!TryParseBbox(param("BBOX"), out var minLon, out var minLat, out var maxLon, out var maxLat))
            return Results.BadRequest("Invalid or missing BBOX.");

        if (!int.TryParse(param("WIDTH"), CultureInfo.InvariantCulture,  out var width)  || width  <= 0) return Results.BadRequest("Invalid WIDTH.");
        if (!int.TryParse(param("HEIGHT"), CultureInfo.InvariantCulture, out var height) || height <= 0) return Results.BadRequest("Invalid HEIGHT.");
        if (!int.TryParse(param("I"), CultureInfo.InvariantCulture,      out var pixelI))                return Results.BadRequest("Missing I (pixel column).");
        if (!int.TryParse(param("J"), CultureInfo.InvariantCulture,      out var pixelJ))                return Results.BadRequest("Missing J (pixel row).");

        var clickLon = minLon + (double)pixelI / width  * (maxLon - minLon);
        var clickLat = maxLat - (double)pixelJ / height * (maxLat - minLat);
        var tolLon   = (maxLon - minLon) * 0.01;
        var tolLat   = (maxLat - minLat) * 0.01;

        var maxCount = int.TryParse(param("FEATURE_COUNT"), CultureInfo.InvariantCulture, out var fc) ? fc : 1;
        var rows     = await renderer.GetFeatureInfoAsync(BoundingBox.Around(clickLon, clickLat, tolLon, tolLat), maxCount, ct).ConfigureAwait(false);

        return Results.Json(new
        {
            type     = "FeatureCollection",
            features = rows.Select(r => new
            {
                id         = r.Id,
                name       = r.Name,
                assetTypeId= r.AssetTypeId,
                description= r.Description
            })
        }, _featureInfoJson);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The map zoom a GetMap request draws at (XD01-430), from its scale: the Web Mercator zoom whose 256-px tiles span
    /// <paramref name="lonSpan"/> degrees of longitude over <paramref name="widthPx"/> pixels — exactly the tile's zoom for
    /// the map's own WMS layer (Leaflet asks EPSG:4326 boxes of 360 / 2^z degrees per 256-px tile), the nearest one for
    /// any other client. Clamped to <see cref="AssetTypeZoomRange"/>'s levels; <c>null</c> (no zoom filtering) for a box
    /// without width.
    /// </summary>
    private static int? ZoomOf(double lonSpan, int widthPx)
    {
        if (!(lonSpan > 0) || widthPx <= 0) return null;

        var zoom = Math.Round(Math.Log2(360.0 * widthPx / (256.0 * lonSpan)), MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(zoom, AssetTypeZoomRange.LowestZoom, AssetTypeZoomRange.HighestZoom);
    }

    private static bool TryParseBbox(string raw,
        out double minLon, out double minLat, out double maxLon, out double maxLat)
    {
        minLon = minLat = maxLon = maxLat = 0;
        var parts = raw.Split(',');
        return parts.Length >= 4
            && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out minLon)
            && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out minLat)
            && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out maxLon)
            && double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out maxLat);
    }
}
