using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GeoAssets.Core.Models.Geometry;
using NetTopologySuite.Geometries;
using SkiaSharp;
#pragma warning disable CA1416 // SkiaSharp is cross-platform

namespace GeoAssets.Server;

/// <summary>
/// Maps an OGC WMS 1.1.1 KVP endpoint onto any <see cref="IEndpointRouteBuilder"/>.
///
/// All data access goes through <see cref="WmsPostGisRenderer"/> which opens a
/// dedicated short-lived <c>GeoAssetsDbContext</c> per request and projects only
/// the columns the renderer needs (geometry + color).  There is no shared cache,
/// no topology or custom-attribute JSONB deserialization — just a single
/// <c>ST_Intersects</c> bbox query straight to PostGIS per tile.
///
/// Supported operations:
/// <list type="bullet">
///   <item><term>GetCapabilities</term><description>WMS 1.1.1 service metadata XML.</description></item>
///   <item><term>GetMap</term><description>PNG raster image rendered with SkiaSharp from PostGIS features.</description></item>
///   <item><term>GetFeatureInfo</term><description>JSON properties for features at a clicked pixel.</description></item>
/// </list>
/// </summary>
public static class WmsEndpointExtensions
{
    // Minimal valid 1×1 transparent PNG returned when the client cancels a tile request.
    // Pre-computed so we never spin up SkiaSharp for a cancelled request.
    private static readonly byte[] _emptyTilePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static IEndpointRouteBuilder MapWmsApi(
        this IEndpointRouteBuilder routes,
        string route = "/wms")
    {
        routes.MapGet(route, async (HttpRequest req, WmsPostGisRenderer renderer, CancellationToken ct) =>
        {
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
                    "GETCAPABILITIES" => await HandleGetCapabilitiesAsync(req, renderer, ct),
                    "GETMAP"          => await HandleGetMapAsync(Param, renderer, ct),
                    "GETFEATUREINFO"  => await HandleGetFeatureInfoAsync(Param, renderer, ct),
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
        });

        return routes;
    }

    // ── GetCapabilities ───────────────────────────────────────────────────────

    private static async Task<IResult> HandleGetCapabilitiesAsync(
        HttpRequest req, WmsPostGisRenderer renderer, CancellationToken ct)
    {
        var baseUrl    = $"{req.Scheme}://{req.Host}{req.PathBase}{req.Path}";
        var assetTypes = await renderer.GetAssetTypesAsync(ct);
        return Results.Content(BuildCapabilitiesXml(baseUrl, assetTypes), "application/xml; charset=utf-8");
    }

    private static string BuildCapabilitiesXml(
        string wmsUrl, List<WmsPostGisRenderer.AssetTypeInfo> assetTypes)
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
                    new XElement("Abstract","OGC WMS 1.1.1 backed by PostGIS"),
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
        Func<string, string> param, WmsPostGisRenderer renderer, CancellationToken ct)
    {
        if (!TryParseBbox(param("BBOX"), out var minLon, out var minLat, out var maxLon, out var maxLat))
            return Results.BadRequest("Invalid or missing BBOX. Expected: minLon,minLat,maxLon,maxLat");

        if (!int.TryParse(param("WIDTH"),  out var width)  || width  <= 0 || width  > 4096)
            return Results.BadRequest("Invalid or missing WIDTH.");
        if (!int.TryParse(param("HEIGHT"), out var height) || height <= 0 || height > 4096)
            return Results.BadRequest("Invalid or missing HEIGHT.");

        var transparent = param("TRANSPARENT").Equals("TRUE", StringComparison.OrdinalIgnoreCase);

        // Resolve optional asset-type filter from LAYERS=geoassets:feature:{typeId}
        var layerParam  = param("LAYERS");
        string? assetTypeId = layerParam.StartsWith("geoassets:feature:", StringComparison.OrdinalIgnoreCase)
            ? layerParam["geoassets:feature:".Length..]
            : null;

        // ── Direct PostGIS query — no cache, only geom + color columns ────────
        var rows = await renderer.GetInBoundsAsync(minLon, minLat, maxLon, maxLat, assetTypeId, ct);

        var pngBytes = RenderToPng(rows, width, height, minLon, minLat, maxLon, maxLat, transparent);
        return Results.Bytes(pngBytes, "image/png");
    }

    // ── GetFeatureInfo ────────────────────────────────────────────────────────

    private static async Task<IResult> HandleGetFeatureInfoAsync(
        Func<string, string> param, WmsPostGisRenderer renderer, CancellationToken ct)
    {
        if (!TryParseBbox(param("BBOX"), out var minLon, out var minLat, out var maxLon, out var maxLat))
            return Results.BadRequest("Invalid or missing BBOX.");

        if (!int.TryParse(param("WIDTH"),  out var width)  || width  <= 0) return Results.BadRequest("Invalid WIDTH.");
        if (!int.TryParse(param("HEIGHT"), out var height) || height <= 0) return Results.BadRequest("Invalid HEIGHT.");
        if (!int.TryParse(param("I"),      out var pixelI))                return Results.BadRequest("Missing I (pixel column).");
        if (!int.TryParse(param("J"),      out var pixelJ))                return Results.BadRequest("Missing J (pixel row).");

        var clickLon = minLon + (double)pixelI / width  * (maxLon - minLon);
        var clickLat = maxLat - (double)pixelJ / height * (maxLat - minLat);
        var tolLon   = (maxLon - minLon) * 0.01;
        var tolLat   = (maxLat - minLat) * 0.01;

        var maxCount = int.TryParse(param("FEATURE_COUNT"), out var fc) ? fc : 1;
        var rows     = await renderer.GetFeatureInfoAsync(clickLon, clickLat, tolLon, tolLat, maxCount, ct);

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
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ── SkiaSharp renderer ────────────────────────────────────────────────────

    private static byte[] RenderToPng(
        List<WmsPostGisRenderer.RenderRow> rows,
        int    width,  int    height,
        double minLon, double minLat, double maxLon, double maxLat,
        bool   transparent)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);

        var lonSpan = maxLon - minLon;
        var latSpan = maxLat - minLat;
        float LonToX(double lon) => (float)((lon - minLon) / lonSpan * width);
        float LatToY(double lat) => (float)((maxLat - lat) / latSpan * height);

        // Draw in z-order: polygons → lines → points (including Multi* variants)
        static bool IsPolygonal(string t) => t is "Polygon" or "MultiPolygon";
        static bool IsLinear(string t)    => t is "LineString" or "MultiLineString";
        static bool IsPoint(string t)     => t is "Point" or "MultiPoint";

        foreach (var row in rows.Where(r => IsPolygonal(r.Geom.GeometryType)))
            DrawGeometry(canvas, row.Geom, SKColor.Parse(row.Color.TrimStart('#')), LonToX, LatToY);
        foreach (var row in rows.Where(r => IsLinear(r.Geom.GeometryType)))
            DrawGeometry(canvas, row.Geom, SKColor.Parse(row.Color.TrimStart('#')), LonToX, LatToY);
        foreach (var row in rows.Where(r => IsPoint(r.Geom.GeometryType)))
            DrawGeometry(canvas, row.Geom, SKColor.Parse(row.Color.TrimStart('#')), LonToX, LatToY);

        using var image = SKImage.FromBitmap(bitmap);
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawGeometry(
        SKCanvas canvas, Geometry geom, SKColor color,
        Func<double, float> lonToX, Func<double, float> latToY)
    {
        switch (geom)
        {
            case Point pt:
                DrawPoint(canvas, pt.X, pt.Y, color, lonToX, latToY);
                break;
            case LineString ls:
                DrawLine(canvas, ls.Coordinates, color, lonToX, latToY);
                break;
            case Polygon poly:
                DrawPolygon(canvas, poly, color, lonToX, latToY);
                break;
            case GeometryCollection gc:
                foreach (var g in gc.Geometries)
                    DrawGeometry(canvas, g, color, lonToX, latToY);
                break;
        }
    }

    private static void DrawPoint(
        SKCanvas canvas, double lon, double lat, SKColor color,
        Func<double, float> lonToX, Func<double, float> latToY)
    {
        var cx = lonToX(lon);
        var cy = latToY(lat);
        using var fill   = new SKPaint { Color = color.WithAlpha(200), IsAntialias = true };
        using var stroke = new SKPaint { Color = color, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        canvas.DrawCircle(cx, cy, 5f, fill);
        canvas.DrawCircle(cx, cy, 5f, stroke);
    }

    private static void DrawLine(
        SKCanvas canvas, Coordinate[] coords, SKColor color,
        Func<double, float> lonToX, Func<double, float> latToY)
    {
        if (coords.Length < 2) return;
        using var path  = CoordinatesToPath(coords, lonToX, latToY);
        using var paint = new SKPaint
        {
            Color       = color,
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap   = SKStrokeCap.Round,
            StrokeJoin  = SKStrokeJoin.Round
        };
        canvas.DrawPath(path, paint);
    }

    private static void DrawPolygon(
        SKCanvas canvas, Polygon poly, SKColor color,
        Func<double, float> lonToX, Func<double, float> latToY)
    {
        using var fillPath  = CoordinatesToPath(poly.ExteriorRing.Coordinates, lonToX, latToY);
        fillPath.Close();
        using var fillPaint = new SKPaint
            { Color = color.WithAlpha(90), IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(fillPath, fillPaint);

        using var strokePaint = new SKPaint
        {
            Color = color, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeJoin = SKStrokeJoin.Round
        };
        // Exterior ring
        canvas.DrawPath(fillPath, strokePaint);
        // Interior rings (holes)
        for (int i = 0; i < poly.NumInteriorRings; i++)
        {
            using var hole = CoordinatesToPath(poly.GetInteriorRingN(i).Coordinates, lonToX, latToY);
            hole.Close();
            canvas.DrawPath(hole, strokePaint);
        }
    }

    private static SKPath CoordinatesToPath(
        Coordinate[] coords, Func<double, float> lonToX, Func<double, float> latToY)
    {
        var path = new SKPath();
        for (int i = 0; i < coords.Length; i++)
        {
            var x = lonToX(coords[i].X);
            var y = latToY(coords[i].Y);
            if (i == 0) path.MoveTo(x, y);
            else        path.LineTo(x, y);
        }
        return path;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
