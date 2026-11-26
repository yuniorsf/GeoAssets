namespace GeoAssets.Core.Rendering;

/// <summary>
/// The spherical Web Mercator projection (EPSG:3857) the tile pyramid is cut from (ADR-002 D6, XD01-431): longitude /
/// latitude (EPSG:4326, degrees) to and from projected meters. Longitudes beyond ±180 and latitudes beyond
/// ±<see cref="MaxLatitude"/> clamp to the square world's edge (the projection is undefined at the poles).
/// </summary>
public static class WebMercator
{
    /// <summary>The sphere's radius in meters (the WGS 84 semi-major axis).</summary>
    public const double EarthRadius = 6_378_137d;

    /// <summary>Half the world's width and height in meters (≈ 20 037 508.34): the projected X of its east edge, Y of its north edge.</summary>
    public const double HalfWorldSize = Math.PI * EarthRadius;

    /// <summary>The latitude of the square world's north edge (≈ 85.0511°), <c>atan(sinh(π))</c> in degrees; the south edge is its negation.</summary>
    public static readonly double MaxLatitude = Math.Atan(Math.Sinh(Math.PI)) * 180d / Math.PI;

    /// <summary>The projected point of (<paramref name="longitude"/>, <paramref name="latitude"/>), clamped to the world.</summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    public static (double X, double Y) Project(double longitude, double latitude)
    {
        EnsureFinite(longitude, nameof(longitude));
        EnsureFinite(latitude, nameof(latitude));

        var lon = Math.Clamp(longitude, -180d, 180d);
        var lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        var x = lon / 180d * HalfWorldSize;
        var y = Math.Log(Math.Tan((90d + lat) * Math.PI / 360d)) * EarthRadius;
        return (x, Math.Clamp(y, -HalfWorldSize, HalfWorldSize));
    }

    /// <summary>The longitude/latitude of the projected point (<paramref name="x"/>, <paramref name="y"/>), clamped to the world.</summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    public static (double Longitude, double Latitude) Unproject(double x, double y)
    {
        EnsureFinite(x, nameof(x));
        EnsureFinite(y, nameof(y));

        var px = Math.Clamp(x, -HalfWorldSize, HalfWorldSize);
        var py = Math.Clamp(y, -HalfWorldSize, HalfWorldSize);
        return (px / HalfWorldSize * 180d, Math.Atan(Math.Sinh(py / EarthRadius)) * 180d / Math.PI);
    }

    /// <summary>The projected envelope of <paramref name="bounds"/>, clamped to the world.</summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    public static MercatorBounds Project(BoundingBox bounds)
    {
        var (minX, minY) = Project(bounds.MinLon, bounds.MinLat);
        var (maxX, maxY) = Project(bounds.MaxLon, bounds.MaxLat);
        return new MercatorBounds(minX, minY, maxX, maxY);
    }

    /// <summary>The longitude/latitude box of <paramref name="bounds"/>, clamped to the world.</summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    public static BoundingBox Unproject(MercatorBounds bounds)
    {
        var (minLon, minLat) = Unproject(bounds.MinX, bounds.MinY);
        var (maxLon, maxLat) = Unproject(bounds.MaxX, bounds.MaxY);
        return new BoundingBox(minLon, minLat, maxLon, maxLat);
    }

    /// <summary>
    /// How many tiles of a <paramref name="tilesPerAxis"/>-wide grid lie west of <paramref name="longitude"/>
    /// (fractional, clamped to the world) — the tile column is its floor.
    /// </summary>
    internal static double TileFractionX(double longitude, int tilesPerAxis)
    {
        EnsureFinite(longitude, nameof(longitude));
        return (Math.Clamp(longitude, -180d, 180d) + 180d) / 360d * tilesPerAxis;
    }

    /// <summary>
    /// How many tiles of the grid lie north of <paramref name="latitude"/> (fractional, clamped to the world) — the tile
    /// row is its floor.
    /// </summary>
    internal static double TileFractionY(double latitude, int tilesPerAxis)
    {
        var (_, y) = Project(0, latitude);
        return (HalfWorldSize - y) / (2 * HalfWorldSize) * tilesPerAxis;
    }

    private static void EnsureFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The coordinate must be a finite number.", paramName);
    }
}
