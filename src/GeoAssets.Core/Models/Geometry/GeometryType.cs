namespace GeoAssets.Core.Models.Geometry;

public enum GeometryType
{
    Point,
    LineString,
    Polygon,
    /// <summary>
    /// Holds any GeoJSON geometry type not natively modelled
    /// (MultiPoint, MultiLineString, MultiPolygon, GeometryCollection, …).
    /// The raw JSON is preserved for map rendering; NTS spatial operations
    /// are not available on this type.
    /// </summary>
    Raw
}
