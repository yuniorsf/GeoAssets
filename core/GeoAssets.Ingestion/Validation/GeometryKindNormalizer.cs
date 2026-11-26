using NetTopologySuite.Geometries;
using CoreGeometryType = GeoAssets.Core.Models.Geometry.GeometryType;

namespace GeoAssets.Ingestion.Validation;

/// <summary>
/// Checks an element's geometry against its asset class's geometry kind: a matching geometry is kept, a single-part
/// collection whose part matches (e.g. a one-line MultiLineString for a LineString class) is unwrapped, anything
/// else — including an empty geometry — is a mismatch. A <see cref="CoreGeometryType.Raw"/> class accepts any geometry.
/// </summary>
internal static class GeometryKindNormalizer
{
    public static Geometry? Normalize(Geometry geometry, CoreGeometryType expected)
    {
        if (geometry.IsEmpty)
        {
            return null;
        }

        if (expected == CoreGeometryType.Raw || Matches(geometry, expected))
        {
            return geometry;
        }

        if (geometry is GeometryCollection { NumGeometries: 1 } collection)
        {
            var part = collection.GetGeometryN(0);
            if (!part.IsEmpty && Matches(part, expected))
            {
                return part;
            }
        }

        return null;
    }

    private static bool Matches(Geometry geometry, CoreGeometryType expected) => expected switch
    {
        CoreGeometryType.Point => geometry is Point,
        CoreGeometryType.LineString => geometry is LineString,
        CoreGeometryType.Polygon => geometry is Polygon,
        _ => false,
    };
}
