namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Infrastructure-free RFC 7946 (<c>[longitude, latitude]</c>) geometry wire snapshot.
/// </summary>
public sealed record AssetGeometrySnapshot(
    WireGeometryKind Kind,
    int Srid,
    IReadOnlyList<double> PointCoordinates,
    IReadOnlyList<IReadOnlyList<double>> LineCoordinates,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> PolygonRings)
{
    public static AssetGeometrySnapshot Point(double longitude, double latitude, int srid = 4326) =>
        new(
            Kind: WireGeometryKind.Point,
            Srid: srid,
            PointCoordinates: [longitude, latitude],
            LineCoordinates: [],
            PolygonRings: []);

    public static AssetGeometrySnapshot LineString(
        IReadOnlyList<IReadOnlyList<double>> positions,
        int srid = 4326)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return new AssetGeometrySnapshot(
            Kind: WireGeometryKind.LineString,
            Srid: srid,
            PointCoordinates: [],
            LineCoordinates: positions,
            PolygonRings: []);
    }

    public static AssetGeometrySnapshot Polygon(
        IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> rings,
        int srid = 4326)
    {
        ArgumentNullException.ThrowIfNull(rings);
        return new AssetGeometrySnapshot(
            Kind: WireGeometryKind.Polygon,
            Srid: srid,
            PointCoordinates: [],
            LineCoordinates: [],
            PolygonRings: rings);
    }
}
