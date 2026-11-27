using GeoAssets.Core.Rendering;

namespace GeoAssets.Bench.Render;

/// <summary>
/// A map viewport the way Leaflet sees it: a center, an integer zoom and a size in CSS pixels, over spherical Web
/// Mercator (EPSG:3857, 256-px tiles). <see cref="Bounds"/> is what <c>map.getBounds()</c> reports — the box the client
/// sends to <c>GET /features/bounds</c>.
/// </summary>
internal sealed class MapViewport
{
    public const int TileSize = 256;
    private const double MaxLatitude = 85.0511287798;

    private readonly double _originX;
    private readonly double _originY;

    public MapViewport(double centerLon, double centerLat, int zoom, int widthPx, int heightPx)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zoom);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);

        CenterLon = centerLon;
        CenterLat = centerLat;
        Zoom = zoom;
        WidthPx = widthPx;
        HeightPx = heightPx;

        // Leaflet's pixel origin: the projected center, minus half the size, rounded.
        var (cx, cy) = Project(centerLon, centerLat);
        _originX = Math.Round(cx - (widthPx / 2.0));
        _originY = Math.Round(cy - (heightPx / 2.0));

        var (minLon, maxLat) = Unproject(_originX, _originY);
        var (maxLon, minLat) = Unproject(_originX + widthPx, _originY + heightPx);
        Bounds = new GeoBounds(minLon, minLat, maxLon, maxLat);
    }

    public double CenterLon { get; }
    public double CenterLat { get; }
    public int Zoom { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }
    public GeoBounds Bounds { get; }

    private double WorldSize => TileSize * Math.Pow(2, Zoom);

    /// <summary>The 256-px tiles a tiled map (MapLibre, Leaflet) requests to fill this viewport: those its pixels overlap (XD01-434).</summary>
    public TileRange CoveringTiles()
    {
        var last = TileAddress.TilesPerAxis(Zoom) - 1;
        int Index(double pixel) => (int)Math.Clamp(Math.Floor(pixel / TileSize), 0, last);
        return new TileRange(Zoom, Index(_originX), Index(_originY), Index(_originX + WidthPx - 1), Index(_originY + HeightPx - 1));
    }

    /// <summary>Absolute world pixel coordinates of a lon/lat at this zoom.</summary>
    public (double X, double Y) Project(double lon, double lat)
    {
        var clamped = Math.Clamp(lat, -MaxLatitude, MaxLatitude) * Math.PI / 180.0;
        var x = (lon + 180.0) / 360.0 * WorldSize;
        var y = (1.0 - (Math.Log(Math.Tan(clamped) + (1.0 / Math.Cos(clamped))) / Math.PI)) / 2.0 * WorldSize;
        return (x, y);
    }

    /// <summary>Leaflet's <c>latLngToLayerPoint</c>: the rounded world pixel, relative to the pixel origin.</summary>
    public (double X, double Y) ToLayerPoint(double lon, double lat)
    {
        var (x, y) = Project(lon, lat);
        return (Math.Round(x) - _originX, Math.Round(y) - _originY);
    }

    private (double Lon, double Lat) Unproject(double x, double y)
    {
        var lon = (x / WorldSize * 360.0) - 180.0;
        var lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - (2.0 * y / WorldSize)))) * 180.0 / Math.PI;
        return (lon, lat);
    }
}
