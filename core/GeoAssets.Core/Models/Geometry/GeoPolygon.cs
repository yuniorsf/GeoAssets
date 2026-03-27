using System.Text.Json.Serialization;
using NtsCoordinate  = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry    = NetTopologySuite.Geometries.Geometry;
using NtsLinearRing  = NetTopologySuite.Geometries.LinearRing;
using NtsPolygon     = NetTopologySuite.Geometries.Polygon;

namespace GeoAssets.Core.Models.Geometry;

public sealed class GeoPolygon : GeoGeometry
{
    private NtsPolygon _polygon;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Parameterless ctor for JSON deserialization.</summary>
    public GeoPolygon() =>
        _polygon = GeoFactory.Wgs84.CreatePolygon(Array.Empty<NtsCoordinate>());

    /// <summary>Wraps an existing NTS polygon — SRID preserved, no coordinate copy.</summary>
    public GeoPolygon(NtsPolygon polygon) => _polygon = polygon;

    /// <summary>
    /// Creates a WGS-84 polygon from a shell ring and optional holes.
    /// Each ring is a sequence of longitude/latitude pairs; the first and last
    /// positions must be identical (closed ring per RFC 7946).
    /// </summary>
    public GeoPolygon(
        IEnumerable<(double Lon, double Lat)> shell,
        IEnumerable<IEnumerable<(double Lon, double Lat)>>? holes = null)
    {
        var shellRing = RingFrom(shell, 4326);
        var holeRings = holes?.Select(h => RingFrom(h, 4326)).ToArray() ?? [];
        _polygon = GeoFactory.Wgs84.CreatePolygon(shellRing, holeRings);
    }

    /// <summary>Creates a polygon in the specified CRS.</summary>
    public GeoPolygon(
        IEnumerable<(double X, double Y)> shell,
        int srid,
        IEnumerable<IEnumerable<(double X, double Y)>>? holes = null)
    {
        var shellRing = RingFrom(shell, srid);
        var holeRings = holes?.Select(h => RingFrom(h, srid)).ToArray() ?? [];
        _polygon = GeoFactory.For(srid).CreatePolygon(shellRing, holeRings);
    }

    // ── GeoGeometry ───────────────────────────────────────────────────────────

    [JsonPropertyName("type")]
    public override string Type => "Polygon";

    [JsonIgnore]
    public override GeometryType GeometryType => GeometryType.Polygon;

    [JsonIgnore]
    public override NtsGeometry NtsGeometry => _polygon;

    /// <summary>Strongly-typed NTS polygon.</summary>
    [JsonIgnore]
    public NtsPolygon Polygon => _polygon;

    // ── Coordinates (JSON) ────────────────────────────────────────────────────

    /// <summary>
    /// Outer ring at [0], optional holes at [1..n].
    /// Getting derives the array from the NTS geometry; setting rebuilds it preserving the current SRID.
    /// </summary>
    [JsonPropertyName("coordinates")]
    public double[][][] Coordinates
    {
        get => ToCoordArray(_polygon);
        set
        {
            var srid = _polygon.SRID;
            if (value.Length == 0)
            {
                _polygon = GeoFactory.For(srid).CreatePolygon(Array.Empty<NtsCoordinate>());
                return;
            }

            var shell = RingFrom(value[0], srid);
            var holes = value.Skip(1).Select(r => RingFrom(r, srid)).ToArray();
            _polygon = GeoFactory.For(srid).CreatePolygon(shell, holes);
        }
    }

    /// <summary>Outer ring coordinates derived directly from the NTS exterior ring.</summary>
    [JsonIgnore]
    public double[][] OuterRing =>
        [.. _polygon.ExteriorRing.Coordinates.Select(static c => new double[] { c.X, c.Y })];

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NtsLinearRing RingFrom(IEnumerable<(double X, double Y)> points, int srid) =>
        GeoFactory.For(srid).CreateLinearRing(
            points.Select(static p => new NtsCoordinate(p.X, p.Y)).ToArray());

    private static NtsLinearRing RingFrom(double[][] ring, int srid) =>
        GeoFactory.For(srid).CreateLinearRing(
            ring.Select(static c => new NtsCoordinate(c[0], c[1])).ToArray());

    private static double[][][] ToCoordArray(NtsPolygon poly)
    {
        var rings = new List<double[][]>
        {
            poly.ExteriorRing.Coordinates.Select(static c => new double[] { c.X, c.Y }).ToArray()
        };

        for (var i = 0; i < poly.NumInteriorRings; i++)
            rings.Add(poly.GetInteriorRingN(i).Coordinates.Select(static c => new double[] { c.X, c.Y }).ToArray());

        return rings.ToArray();
    }
}
