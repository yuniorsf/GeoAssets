using GeoAssets.Core.Rendering;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace GeoAssets.Server.Tests.Wms;

/// <summary>
/// The WMS GetMap renderer exactly as it was inside <c>WmsEndpointExtensions</c> before XD01-433 extracted it into
/// <c>SkiaFeaturePainter</c> — kept verbatim as the oracle <see cref="WmsRenderingRegressionTests"/> compares the
/// endpoint's images with.
/// </summary>
internal static class PreExtractionWmsRenderer
{
    public static byte[] RenderToPng(
        IReadOnlyList<RenderableFeature> rows,
        int width, int height,
        double minLon, double minLat, double maxLon, double maxLat,
        bool transparent)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);

        var lonSpan = maxLon - minLon;
        var latSpan = maxLat - minLat;
        float LonToX(double lon) => (float)((lon - minLon) / lonSpan * width);
        float LatToY(double lat) => (float)((maxLat - lat) / latSpan * height);

        static bool IsPolygonal(string t) => t is "Polygon" or "MultiPolygon";
        static bool IsLinear(string t) => t is "LineString" or "MultiLineString";
        static bool IsPoint(string t) => t is "Point" or "MultiPoint";

        foreach (var row in rows.Where(r => IsPolygonal(r.Geometry.NtsGeometry.GeometryType)))
            DrawGeometry(canvas, row.Geometry.NtsGeometry, SKColor.Parse(row.Color.TrimStart('#')), LonToX, LatToY);
        foreach (var row in rows.Where(r => IsLinear(r.Geometry.NtsGeometry.GeometryType)))
            DrawGeometry(canvas, row.Geometry.NtsGeometry, SKColor.Parse(row.Color.TrimStart('#')), LonToX, LatToY);
        foreach (var row in rows.Where(r => IsPoint(r.Geometry.NtsGeometry.GeometryType)))
            DrawGeometry(canvas, row.Geometry.NtsGeometry, SKColor.Parse(row.Color.TrimStart('#')), LonToX, LatToY);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
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
        using var fill = new SKPaint { Color = color.WithAlpha(200), IsAntialias = true };
        using var stroke = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        canvas.DrawCircle(cx, cy, 5f, fill);
        canvas.DrawCircle(cx, cy, 5f, stroke);
    }

    private static void DrawLine(
        SKCanvas canvas, Coordinate[] coords, SKColor color,
        Func<double, float> lonToX, Func<double, float> latToY)
    {
        if (coords.Length < 2) return;
        using var path = CoordinatesToPath(coords, lonToX, latToY);
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(path, paint);
    }

    private static void DrawPolygon(
        SKCanvas canvas, Polygon poly, SKColor color,
        Func<double, float> lonToX, Func<double, float> latToY)
    {
        using var fillPath = CoordinatesToPath(poly.ExteriorRing.Coordinates, lonToX, latToY);
        fillPath.Close();
        using var fillPaint = new SKPaint { Color = color.WithAlpha(90), IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(fillPath, fillPaint);

        using var strokePaint = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(fillPath, strokePaint);
        for (int i = 0; i < poly.NumInteriorRings; i++)
        {
            using var hole = CoordinatesToPath(poly.GetInteriorRingN(i).Coordinates, lonToX, latToY);
            hole.Close();
            canvas.DrawPath(hole, strokePaint);
        }
    }

    private static SKPath CoordinatesToPath(Coordinate[] coords, Func<double, float> lonToX, Func<double, float> latToY)
    {
        var path = new SKPath();
        for (int i = 0; i < coords.Length; i++)
        {
            var x = lonToX(coords[i].X);
            var y = latToY(coords[i].Y);
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        return path;
    }
}
