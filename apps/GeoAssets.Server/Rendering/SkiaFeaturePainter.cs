using GeoAssets.Core.Rendering;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace GeoAssets.Server.Rendering;

/// <summary>
/// The SkiaSharp painter of map images (XD01-433, extracted from the WMS GetMap renderer): draws
/// <see cref="RenderableFeature"/>s in the color their render query resolved (<see cref="RenderStyle"/>, i.e.
/// <see cref="Core.Services.LayerResolver"/>) onto a canvas, through a <see cref="PixelTransform"/> — so the WMS image
/// (EPSG:4326) and the raster tiles (<see cref="SkiaRasterTileSource"/>, EPSG:3857) draw a feature alike. Within one
/// call polygons go first, then lines, then points (<c>Multi*</c> and collections included): a point is a 5 px circle
/// (fill at alpha 200, 1.5 px outline), a line a 2 px round-capped stroke, a polygon a fill at alpha 90 with a 1.5 px
/// outline of every ring. Nothing reaches more than <see cref="MaxSymbolOverhang"/> pixels past its geometry.
/// </summary>
public static class SkiaFeaturePainter
{
    /// <summary>The radius of a point's circle, in pixels.</summary>
    public const float PointRadius = 5f;

    /// <summary>
    /// How far, in pixels, a drawn feature can reach past its geometry — a point's circle and half its outline — so a
    /// tile renderer reading this much past its edge draws every symbol that shows in it.
    /// </summary>
    public const float MaxSymbolOverhang = PointRadius + (PointStrokeWidth / 2);

    private const float PointStrokeWidth = 1.5f;
    private const float LineStrokeWidth = 2f;
    private const float PolygonStrokeWidth = 1.5f;

    /// <summary>
    /// A <paramref name="width"/> × <paramref name="height"/> PNG of <paramref name="features"/>, on a transparent
    /// background when <paramref name="transparent"/>, else a white one — the WMS GetMap image.
    /// </summary>
    public static byte[] RenderPng(
        IReadOnlyList<RenderableFeature> features, int width, int height, PixelTransform transform, bool transparent)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(transform);

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
        Paint(canvas, features, transform);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Draws <paramref name="features"/> onto <paramref name="canvas"/>: polygons, then lines, then points.</summary>
    public static void Paint(SKCanvas canvas, IReadOnlyList<RenderableFeature> features, PixelTransform transform)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(transform);

        static bool IsPolygonal(string t) => t is "Polygon" or "MultiPolygon";
        static bool IsLinear(string t) => t is "LineString" or "MultiLineString";
        static bool IsPoint(string t) => t is "Point" or "MultiPoint";

        foreach (var feature in features.Where(f => IsPolygonal(f.Geometry.NtsGeometry.GeometryType)))
            DrawGeometry(canvas, feature.Geometry.NtsGeometry, ColorOf(feature), transform);
        foreach (var feature in features.Where(f => IsLinear(f.Geometry.NtsGeometry.GeometryType)))
            DrawGeometry(canvas, feature.Geometry.NtsGeometry, ColorOf(feature), transform);
        foreach (var feature in features.Where(f => IsPoint(f.Geometry.NtsGeometry.GeometryType)))
            DrawGeometry(canvas, feature.Geometry.NtsGeometry, ColorOf(feature), transform);
    }

    private static SKColor ColorOf(RenderableFeature feature) => SKColor.Parse(feature.Color.TrimStart('#'));

    private static void DrawGeometry(SKCanvas canvas, Geometry geom, SKColor color, PixelTransform transform)
    {
        switch (geom)
        {
            case Point pt:
                DrawPoint(canvas, pt.X, pt.Y, color, transform);
                break;
            case LineString ls:
                DrawLine(canvas, ls.Coordinates, color, transform);
                break;
            case Polygon poly:
                DrawPolygon(canvas, poly, color, transform);
                break;
            case GeometryCollection gc:
                foreach (var g in gc.Geometries)
                    DrawGeometry(canvas, g, color, transform);
                break;
        }
    }

    private static void DrawPoint(SKCanvas canvas, double lon, double lat, SKColor color, PixelTransform transform)
    {
        var cx = transform.X(lon);
        var cy = transform.Y(lat);
        using var fill = new SKPaint { Color = color.WithAlpha(200), IsAntialias = true };
        using var stroke = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = PointStrokeWidth,
        };
        canvas.DrawCircle(cx, cy, PointRadius, fill);
        canvas.DrawCircle(cx, cy, PointRadius, stroke);
    }

    private static void DrawLine(SKCanvas canvas, Coordinate[] coords, SKColor color, PixelTransform transform)
    {
        if (coords.Length < 2) return;
        using var path = CoordinatesToPath(coords, transform);
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = LineStrokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(path, paint);
    }

    private static void DrawPolygon(SKCanvas canvas, Polygon poly, SKColor color, PixelTransform transform)
    {
        using var fillPath = CoordinatesToPath(poly.ExteriorRing.Coordinates, transform);
        fillPath.Close();
        using var fillPaint = new SKPaint { Color = color.WithAlpha(90), IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(fillPath, fillPaint);

        using var strokePaint = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = PolygonStrokeWidth, StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawPath(fillPath, strokePaint);
        for (int i = 0; i < poly.NumInteriorRings; i++)
        {
            using var hole = CoordinatesToPath(poly.GetInteriorRingN(i).Coordinates, transform);
            hole.Close();
            canvas.DrawPath(hole, strokePaint);
        }
    }

    private static SKPath CoordinatesToPath(Coordinate[] coords, PixelTransform transform)
    {
        var path = new SKPath();
        for (int i = 0; i < coords.Length; i++)
        {
            var x = transform.X(coords[i].X);
            var y = transform.Y(coords[i].Y);
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        return path;
    }
}
