using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Real-world (meters) great-circle distance between two WGS-84 points, via the Haversine
/// formula — <see cref="GeoGeometry.Distance"/> is planar Euclidean distance in the geometry's
/// own coordinate units (degrees for the lon/lat SRID 4326 every <see cref="GeoPoint"/> in this
/// codebase uses, per <c>GeoGeometry.NtsGeometry.Distance</c>), not meters, and is not
/// geodetically accurate — it does not account for the Earth's curvature or for longitude
/// degrees covering less ground distance away from the equator.
///
/// Uses the mean Earth radius (a sphere, not WGS-84's true spheroid) — accurate to within
/// ~0.5% worldwide, the same approximation PostGIS's <c>ST_Distance</c>/<c>ST_DWithin</c> use
/// when <c>use_spheroid = false</c> (their default for a <c>geography</c> comparison below a
/// few hundred km). Good enough for a field-crew arrival tolerance measured in tens of meters;
/// XD01-221's <c>PostgresAssetProvider</c> override additionally has the option to delegate to a
/// true server-side <c>ST_DWithin</c> for full spheroidal accuracy at scale — this calculator is
/// the shared fallback every other <see cref="GeoAssets.Core.Interfaces.IAssetProvider"/>
/// implementation (in-memory, REST, WFS, …) uses by default.
/// </summary>
public static class GeodeticDistanceCalculator
{
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>Great-circle distance between <paramref name="a"/> and <paramref name="b"/>, in meters.</summary>
    public static double DistanceInMeters(GeoPoint a, GeoPoint b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);
        var deltaLat = DegreesToRadians(b.Latitude - a.Latitude);
        var deltaLon = DegreesToRadians(b.Longitude - a.Longitude);

        var sinHalfLat = Math.Sin(deltaLat / 2);
        var sinHalfLon = Math.Sin(deltaLon / 2);
        var h = sinHalfLat * sinHalfLat + Math.Cos(lat1) * Math.Cos(lat2) * sinHalfLon * sinHalfLon;

        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are within <paramref name="toleranceMeters"/> of each other (boundary-inclusive, matching this codebase's other spatial predicates — e.g. <see cref="GeoGeometry.Within"/>).</summary>
    public static bool IsWithinDistance(GeoPoint a, GeoPoint b, double toleranceMeters) =>
        DistanceInMeters(a, b) <= toleranceMeters;

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
