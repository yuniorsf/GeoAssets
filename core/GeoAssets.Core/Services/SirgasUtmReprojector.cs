using GeoAssets.Core.Models.Geometry;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GeoAssets.Core.Services;

/// <summary>
/// Reprojects a WGS84 (SRID 4326) point — the CRS every <see cref="GeoPoint"/> in this codebase
/// stores — into its SIRGAS 2000 / UTM zone, for ANEEL BDGD exports (XD01-224/225). Uses the
/// same ProjNET package (and WKT dialect — ESRI-style <c>PARAMETER</c> names, proven to parse
/// correctly against this exact ProjNET version) that
/// <c>GeoAssets.Provider.Shapefile.ShapefileParser</c> already uses for the reverse direction
/// (an arbitrary source CRS → WGS84).
///
/// SIRGAS 2000's geodetic reference frame differs from WGS84 by only a few centimeters at most
/// latitudes — negligible next to BDGD's own documented ±0.5m tolerance — so this treats WGS84
/// coordinates as directly usable SIRGAS 2000 geographic coordinates rather than applying a
/// separate datum transformation, the same simplification most practical BDGD tooling makes.
/// </summary>
public static class SirgasUtmReprojector
{
    private static readonly Dictionary<int, MathTransform> TransformsByZone = [];
    private static readonly System.Threading.Lock BuildLock = new();

    /// <summary>The point's easting/northing (meters) in its own SIRGAS 2000 UTM zone, plus that zone's EPSG code.</summary>
    public static (double Easting, double Northing, int EpsgCode) ToSirgasUtm(GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var zone = SirgasUtmZoneResolver.ZoneFor(point.Longitude);
        var epsgCode = SirgasUtmZoneResolver.EpsgCodeFor(point.Longitude);
        var transform = GetOrBuildTransform(zone);

        var (easting, northing) = transform.Transform(point.Longitude, point.Latitude);
        return (easting, northing, epsgCode);
    }

    /// <summary>The inverse of <see cref="ToSirgasUtm"/> — mainly for round-trip accuracy testing.</summary>
    public static GeoPoint FromSirgasUtm(double easting, double northing, int zone)
    {
        var inverse = GetOrBuildTransform(zone).Inverse();
        var (longitude, latitude) = inverse.Transform(easting, northing);
        return new GeoPoint(longitude, latitude);
    }

    /// <summary>
    /// The same WGS84 → SIRGAS 2000 UTM transform <see cref="ToSirgasUtm"/> applies to a
    /// <see cref="GeoPoint"/>, exposed for a raw longitude/latitude pair — for reprojecting every
    /// vertex of a non-point geometry (e.g. a BDGD SEGCON <see cref="GeoLineString"/>, XD01-225)
    /// into the same zone, one coordinate at a time.
    /// </summary>
    public static (double Easting, double Northing) TransformCoordinate(double longitude, double latitude, int zone)
    {
        var transform = GetOrBuildTransform(zone);
        return transform.Transform(longitude, latitude);
    }

    private static MathTransform GetOrBuildTransform(int zone)
    {
        lock (BuildLock)
        {
            if (TransformsByZone.TryGetValue(zone, out var existing))
                return existing;

            var targetCs = new CoordinateSystemFactory().CreateFromWkt(SirgasUtmWkt(zone));
            var transform = new CoordinateTransformationFactory()
                .CreateFromCoordinateSystems(GeographicCoordinateSystem.WGS84, targetCs)
                .MathTransform;

            TransformsByZone[zone] = transform;
            return transform;
        }
    }

    /// <summary>
    /// Public wrapper over <see cref="SirgasUtmWkt"/> (XD01-284) — for a caller writing its own
    /// sidecar file (e.g. a Shapefile's <c>.prj</c>) that needs the same WKT this reprojector
    /// itself builds its <see cref="MathTransform"/> from, rather than re-deriving it.
    /// </summary>
    public static string WktFor(int zone) => SirgasUtmWkt(zone);

    /// <summary>
    /// ESRI-style WKT for SIRGAS 2000 / UTM zone {zone}S — same dialect (capitalized
    /// <c>Transverse_Mercator</c>/<c>Central_Meridian</c>/etc. parameter names) as the real-world
    /// SAD69 UTM fixture already proven to parse correctly against this ProjNET version in
    /// <c>ShapefileParserProjectionTests</c>. GRS 1980 is SIRGAS 2000's defining spheroid.
    /// </summary>
    private static string SirgasUtmWkt(int zone)
    {
        var centralMeridian = SirgasUtmZoneResolver.CentralMeridianFor(zone);
        return $"PROJCS[\"SIRGAS_2000_UTM_Zone_{zone}S\",GEOGCS[\"GCS_SIRGAS_2000\",DATUM[\"D_SIRGAS_2000\"," +
               "SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]," +
               "PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",10000000.0]," +
               $"PARAMETER[\"Central_Meridian\",{centralMeridian}],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
    }
}
