using GeoAssets.Ingestion.Spatial;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Projection;

/// <summary>Transforms a native geometry to geographic coordinates and guarantees SRID 4326 on the result.</summary>
internal static class Reprojection
{
    public const int GeographicSrid = 4326;

    public static Geometry ToGeographic(ICoordinateTransformer transformer, Geometry nativeGeometry)
    {
        var transformed = transformer.Transform(nativeGeometry);
        if (transformed.SRID == GeographicSrid)
        {
            return transformed;
        }

        var copy = transformed.Copy();
        copy.SRID = GeographicSrid;
        return copy;
    }
}
