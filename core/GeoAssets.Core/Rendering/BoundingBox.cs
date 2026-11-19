using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>A longitude/latitude bounding box (SRID 4326), edges inclusive (XD01-372).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BoundingBox(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    /// <summary>The box of half-size (<paramref name="toleranceLon"/>, <paramref name="toleranceLat"/>) around a point.</summary>
    public static BoundingBox Around(double lon, double lat, double toleranceLon, double toleranceLat) =>
        new(lon - toleranceLon, lat - toleranceLat, lon + toleranceLon, lat + toleranceLat);
}
