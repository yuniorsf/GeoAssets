using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Rendering;

/// <summary>
/// Where a longitude/latitude lands on an image (XD01-433): a pixel column from the longitude alone and a pixel row from
/// the latitude alone, row 0 at the image's north edge. <see cref="LonLat"/> is the plate carrée of a WMS GetMap box
/// (EPSG:4326); <see cref="WebMercator"/> the Web Mercator plane (EPSG:3857) a tile pyramid is cut from.
/// </summary>
public sealed class PixelTransform
{
    private readonly Func<double, float> _column;
    private readonly Func<double, float> _row;

    private PixelTransform(Func<double, float> column, Func<double, float> row)
    {
        _column = column;
        _row = row;
    }

    /// <summary>The pixel column of <paramref name="longitude"/>.</summary>
    public float X(double longitude) => _column(longitude);

    /// <summary>The pixel row of <paramref name="latitude"/>.</summary>
    public float Y(double latitude) => _row(latitude);

    /// <summary>
    /// <paramref name="box"/> stretched linearly in degrees over a <paramref name="width"/> × <paramref name="height"/>
    /// image — what a WMS GetMap in EPSG:4326 draws.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    public static PixelTransform LonLat(BoundingBox box, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var lonSpan = box.MaxLon - box.MinLon;
        var latSpan = box.MaxLat - box.MinLat;
        return new PixelTransform(
            lon => (float)((lon - box.MinLon) / lonSpan * width),
            lat => (float)((box.MaxLat - lat) / latSpan * height));
    }

    /// <summary>
    /// The Web Mercator plane at <paramref name="metersPerPixel"/>, with the image's top-left corner at the projected
    /// point (<paramref name="originX"/>, <paramref name="originY"/>). Coordinates beyond the square world clamp to its
    /// edge (<see cref="Core.Rendering.WebMercator.Project(double, double)"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="metersPerPixel"/> is not a positive finite number.</exception>
    public static PixelTransform WebMercator(double originX, double originY, double metersPerPixel)
    {
        if (!double.IsFinite(metersPerPixel) || metersPerPixel <= 0)
            throw new ArgumentOutOfRangeException(nameof(metersPerPixel), metersPerPixel, "The resolution must be a positive finite number.");

        return new PixelTransform(
            lon => (float)((Core.Rendering.WebMercator.Project(lon, 0).X - originX) / metersPerPixel),
            lat => (float)((originY - Core.Rendering.WebMercator.Project(0, lat).Y) / metersPerPixel));
    }
}
