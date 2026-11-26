using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>The native point an element is indexed at: the point itself for points, else the centroid.</summary>
internal static class RepresentativePoint
{
    public static bool TryGet(Geometry geometry, out double x, out double y)
    {
        x = double.NaN;
        y = double.NaN;
        if (geometry.IsEmpty)
        {
            return false;
        }

        var point = geometry as Point ?? geometry.Centroid;
        if (point is null || point.IsEmpty || double.IsNaN(point.X) || double.IsNaN(point.Y))
        {
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }
}
