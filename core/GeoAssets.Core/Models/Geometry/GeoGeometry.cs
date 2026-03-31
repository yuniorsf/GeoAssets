using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.IO.Converters;
using NtsGeometry   = NetTopologySuite.Geometries.Geometry;
using NtsLineString = NetTopologySuite.Geometries.LineString;
using NtsPoint      = NetTopologySuite.Geometries.Point;
using NtsPolygon    = NetTopologySuite.Geometries.Polygon;

namespace GeoAssets.Core.Models.Geometry;

/// <summary>
/// Abstract base for all GeoAssets geometry types.
///
/// The NTS geometry is the <b>source of truth</b>: coordinate arrays are derived
/// from it for JSON serialization rather than the other way around.  This means:
/// <list type="bullet">
///   <item>No lazy re-building — the NTS object is set once at construction time.</item>
///   <item>SRID travels with the geometry via <see cref="NtsGeometry.SRID"/>.</item>
///   <item><see cref="FromNts"/> wraps an existing NTS object directly with zero copies.</item>
/// </list>
///
/// GeoJSON serialization always emits coordinate arrays without an explicit CRS node,
/// conforming to RFC 7946.  When deserializing, coordinates are assumed to be WGS-84
/// (SRID 4326).  For projected geometries, construct with an NTS overload.
/// </summary>
public abstract class GeoGeometry
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonIgnore]
    public abstract GeometryType GeometryType { get; }

    /// <summary>The underlying NTS geometry — SRID, validity, and all spatial state live here.</summary>
    [JsonIgnore]
    public abstract NtsGeometry NtsGeometry { get; }

    /// <summary>SRID of the underlying NTS geometry. Defaults to 4326 (WGS-84).</summary>
    [JsonIgnore]
    public int Srid => NtsGeometry.SRID;

    // ── Bounding box ──────────────────────────────────────────────────────────

    /// <summary>Returns [minLon, minLat, maxLon, maxLat] (RFC 7946 §5).</summary>
    public double[] GetBoundingBox()
    {
        var env = NtsGeometry.EnvelopeInternal;
        return env.IsNull ? [0, 0, 0, 0] : [env.MinX, env.MinY, env.MaxX, env.MaxY];
    }

    // ── Spatial predicates ────────────────────────────────────────────────────

    public bool Contains(GeoGeometry other)        => NtsGeometry.Contains(other.NtsGeometry);
    public bool Intersects(GeoGeometry other)      => NtsGeometry.Intersects(other.NtsGeometry);
    public bool Crosses(GeoGeometry other)         => NtsGeometry.Crosses(other.NtsGeometry);
    public bool Overlaps(GeoGeometry other)        => NtsGeometry.Overlaps(other.NtsGeometry);
    public bool Touches(GeoGeometry other)         => NtsGeometry.Touches(other.NtsGeometry);
    public bool Within(GeoGeometry other)          => NtsGeometry.Within(other.NtsGeometry);
    public bool CoveredBy(GeoGeometry other)       => NtsGeometry.CoveredBy(other.NtsGeometry);
    public bool Covers(GeoGeometry other)          => NtsGeometry.Covers(other.NtsGeometry);
    public bool Disjoint(GeoGeometry other)        => NtsGeometry.Disjoint(other.NtsGeometry);

    // ── Measurements ─────────────────────────────────────────────────────────

    /// <summary>
    /// Cartesian distance in the geometry's coordinate units
    /// (degrees for SRID 4326, metres for projected CRS).
    /// </summary>
    public double Distance(GeoGeometry other)      => NtsGeometry.Distance(other.NtsGeometry);

    /// <summary>Planar area in the geometry's coordinate units squared.</summary>
    [JsonIgnore] public double Area   => NtsGeometry.Area;

    /// <summary>Planar length in the geometry's coordinate units.</summary>
    [JsonIgnore] public double Length => NtsGeometry.Length;

    // ── Topology ──────────────────────────────────────────────────────────────

    [JsonIgnore] public bool IsValid => NtsGeometry.IsValid;
    [JsonIgnore] public bool IsEmpty => NtsGeometry.IsEmpty;

    // ── Derived geometries ────────────────────────────────────────────────────

    /// <summary>Geometric centroid as a <see cref="GeoPoint"/> in the same SRID.</summary>
    [JsonIgnore]
    public GeoPoint Centroid
    {
        get
        {
            var c = NtsGeometry.Centroid;
            return new GeoPoint(c);
        }
    }

    public GeoGeometry Buffer(double distance)                 => FromNts(NtsGeometry.Buffer(distance));
    public GeoGeometry ConvexHull()                            => FromNts(NtsGeometry.ConvexHull());
    public GeoGeometry Intersection(GeoGeometry other)        => FromNts(NtsGeometry.Intersection(other.NtsGeometry));
    public GeoGeometry Union(GeoGeometry other)               => FromNts(NtsGeometry.Union(other.NtsGeometry));
    public GeoGeometry Difference(GeoGeometry other)          => FromNts(NtsGeometry.Difference(other.NtsGeometry));
    public GeoGeometry SymmetricDifference(GeoGeometry other) => FromNts(NtsGeometry.SymmetricDifference(other.NtsGeometry));

    // ── NTS ↔ GeoGeometry ────────────────────────────────────────────────────

    internal static readonly JsonSerializerOptions _ntsJsonOpts = new()
    {
        Converters = { new GeoJsonConverterFactory() }
    };

    /// <summary>
    /// Wraps an NTS geometry as a <see cref="GeoGeometry"/>.
    /// Point, LineString, and Polygon map to their typed subclasses.
    /// All other types (MultiPolygon, GeometryCollection, …) are serialized
    /// to GeoJSON and stored as <see cref="GeoRawGeometry"/> for pass-through
    /// rendering — spatial predicates are not supported on raw geometries.
    /// </summary>
    public static GeoGeometry FromNts(NtsGeometry nts) => nts switch
    {
        NtsPoint      p    => new GeoPoint(p),
        NtsLineString ls   => new GeoLineString(ls),
        NtsPolygon    poly => new GeoPolygon(poly),
        _ => new GeoRawGeometry(
                nts.GeometryType,
                JsonSerializer.Serialize(nts, _ntsJsonOpts))
    };
}
