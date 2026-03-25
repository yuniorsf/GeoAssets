using System.Text.Json.Serialization;
using NtsCoordinate  = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry    = NetTopologySuite.Geometries.Geometry;
using NtsLineString  = NetTopologySuite.Geometries.LineString;

namespace GeoAssets.Core.Models.Geometry;

public sealed class GeoLineString : GeoGeometry
{
    private NtsLineString _line;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Parameterless ctor for JSON deserialization.</summary>
    public GeoLineString() =>
        _line = GeoFactory.Wgs84.CreateLineString(Array.Empty<NtsCoordinate>());

    /// <summary>Wraps an existing NTS line string — SRID preserved, no coordinate copy.</summary>
    public GeoLineString(NtsLineString lineString) => _line = lineString;

    /// <summary>Creates a WGS-84 line string from longitude/latitude pairs.</summary>
    public GeoLineString(IEnumerable<(double Lon, double Lat)> points) =>
        _line = GeoFactory.Wgs84.CreateLineString(
            points.Select(static p => new NtsCoordinate(p.Lon, p.Lat)).ToArray());

    /// <summary>Creates a line string in the specified CRS.</summary>
    public GeoLineString(IEnumerable<(double X, double Y)> points, int srid) =>
        _line = GeoFactory.For(srid).CreateLineString(
            points.Select(static p => new NtsCoordinate(p.X, p.Y)).ToArray());

    // ── GeoGeometry ───────────────────────────────────────────────────────────

    [JsonPropertyName("type")]
    public override string Type => "LineString";

    [JsonIgnore]
    public override GeometryType GeometryType => GeometryType.LineString;

    [JsonIgnore]
    public override NtsGeometry NtsGeometry => _line;

    /// <summary>Strongly-typed NTS line string.</summary>
    [JsonIgnore]
    public NtsLineString LineString => _line;

    // ── Coordinates (JSON) ────────────────────────────────────────────────────

    /// <summary>
    /// Array of [longitude, latitude] positions — RFC 7946 §3.1.4.
    /// Getting derives the array from the NTS geometry; setting rebuilds it in WGS-84.
    /// </summary>
    [JsonPropertyName("coordinates")]
    public double[][] Coordinates
    {
        get => [.. _line.Coordinates.Select(static c => new double[] { c.X, c.Y })];
        set => _line = value.Length < 2
            ? GeoFactory.Wgs84.CreateLineString(Array.Empty<NtsCoordinate>())
            : GeoFactory.Wgs84.CreateLineString(
                value.Select(static c => new NtsCoordinate(c[0], c[1])).ToArray());
    }
}
