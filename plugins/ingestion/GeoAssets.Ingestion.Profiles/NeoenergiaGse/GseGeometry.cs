using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>Representative points of native-CRS record geometries (for snap references and coordinate keys).</summary>
internal static class GseGeometry
{
    /// <summary>The point itself for points (the first one of a multi-point), else the centroid.</summary>
    public static Point Representative(Geometry geometry) => geometry switch
    {
        Point point => point,
        MultiPoint { NumGeometries: > 0 } multiPoint => (Point)multiPoint.GetGeometryN(0),
        _ => geometry.Centroid,
    };

    /// <summary>The first coordinate of a line (of its first part, for a multi-line).</summary>
    public static Point Start(Geometry geometry)
    {
        var coordinates = geometry.Coordinates;
        return coordinates.Length == 0 ? geometry.Centroid : geometry.Factory.CreatePoint(coordinates[0].Copy());
    }

    /// <summary>The last coordinate of a line (of its last part, for a multi-line).</summary>
    public static Point End(Geometry geometry)
    {
        var coordinates = geometry.Coordinates;
        return coordinates.Length == 0 ? geometry.Centroid : geometry.Factory.CreatePoint(coordinates[^1].Copy());
    }
}
