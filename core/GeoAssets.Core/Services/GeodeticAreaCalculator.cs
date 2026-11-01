using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Real-world (square meters) area of a small WGS-84 polygon — <see cref="GeoGeometry.Area"/>
/// is NTS's native planar area in the geometry's own coordinate units (square *degrees* for the
/// lon/lat SRID 4326 every <see cref="GeoPolygon"/> in this codebase uses), not square meters,
/// same reason <see cref="GeodeticDistanceCalculator"/> exists for distance instead of reusing
/// <see cref="GeoGeometry.Distance"/> directly.
///
/// Projects each vertex to local meters via a simple equirectangular approximation — scale
/// longitude degrees by <c>cos(referenceLatitude)</c>, latitude degrees by a constant — centered
/// on the polygon's own first vertex, then applies the shoelace formula. This is not a true
/// geodesic/spheroidal area (what PostGIS's <c>ST_Area(geography)</c> computes): at the scale
/// this is meant for (a field-crew's excavation footprint, a few square meters), Earth's
/// curvature is negligible and the equirectangular approximation is effectively exact — it is
/// not appropriate for a polygon spanning any real fraction of the globe.
/// </summary>
public static class GeodeticAreaCalculator
{
    private const double MetersPerDegreeLatitude = 111_320.0;

    public static double AreaSquareMeters(GeoPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var ring = polygon.Polygon.Shell.Coordinates;
        if (ring.Length < 4) return 0; // not a real closed ring (needs >= 3 distinct vertices + closing point)

        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(ring[0].Y * Math.PI / 180.0);

        var signedAreaSumSquareMeters = 0.0;
        for (var i = 0; i < ring.Length - 1; i++)
        {
            var x1 = ring[i].X * metersPerDegreeLongitude;
            var y1 = ring[i].Y * MetersPerDegreeLatitude;
            var x2 = ring[i + 1].X * metersPerDegreeLongitude;
            var y2 = ring[i + 1].Y * MetersPerDegreeLatitude;
            signedAreaSumSquareMeters += (x1 * y2) - (x2 * y1);
        }

        return Math.Abs(signedAreaSumSquareMeters) / 2.0;
    }
}
