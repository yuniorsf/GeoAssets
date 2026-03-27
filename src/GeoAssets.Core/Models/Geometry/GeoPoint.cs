using System.Text.Json.Serialization;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry   = NetTopologySuite.Geometries.Geometry;
using NtsPoint      = NetTopologySuite.Geometries.Point;

namespace GeoAssets.Core.Models.Geometry;

public sealed class GeoPoint : GeoGeometry
{
    private NtsPoint _point;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Parameterless ctor for JSON deserialization (sets <see cref="Coordinates"/> via init).</summary>
    public GeoPoint() =>
        _point = GeoFactory.Wgs84.CreatePoint(new NtsCoordinate(0, 0));

    /// <summary>Wraps an existing NTS point — SRID preserved, no coordinate copy.</summary>
    public GeoPoint(NtsPoint point) => _point = point;

    /// <summary>Creates a WGS-84 point from longitude / latitude.</summary>
    public GeoPoint(double lon, double lat) =>
        _point = GeoFactory.Wgs84.CreatePoint(new NtsCoordinate(lon, lat));

    /// <summary>Creates a point in the specified CRS.</summary>
    public GeoPoint(double lon, double lat, int srid) =>
        _point = GeoFactory.For(srid).CreatePoint(new NtsCoordinate(lon, lat));

    // ── GeoGeometry ───────────────────────────────────────────────────────────

    [JsonPropertyName("type")]
    public override string Type => "Point";

    [JsonIgnore]
    public override GeometryType GeometryType => GeometryType.Point;

    [JsonIgnore]
    public override NtsGeometry NtsGeometry => _point;

    /// <summary>Strongly-typed NTS point (avoids a cast when the caller knows the subtype).</summary>
    [JsonIgnore]
    public NtsPoint Point => _point;

    // ── Coordinates (JSON + convenience) ─────────────────────────────────────

    /// <summary>
    /// [X, Y] position in the geometry's CRS.
    /// For WGS-84 this is [longitude, latitude]; for projected CRS it is [easting, northing] (or similar).
    /// Setting rebuilds the NTS point preserving the current SRID.
    /// </summary>
    [JsonPropertyName("coordinates")]
    public double[] Coordinates
    {
        get => [_point.X, _point.Y];
        set => _point = GeoFactory.For(_point.SRID).CreatePoint(new NtsCoordinate(value[0], value[1]));
    }

    [JsonIgnore] public double Longitude => _point.X;
    [JsonIgnore] public double Latitude  => _point.Y;
}
