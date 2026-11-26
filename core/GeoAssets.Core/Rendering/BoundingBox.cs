using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>A longitude/latitude bounding box (SRID 4326), edges inclusive (XD01-372).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BoundingBox(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    /// <summary>The box of half-size (<paramref name="toleranceLon"/>, <paramref name="toleranceLat"/>) around a point.</summary>
    public static BoundingBox Around(double lon, double lat, double toleranceLon, double toleranceLat) =>
        new(lon - toleranceLon, lat - toleranceLat, lon + toleranceLon, lat + toleranceLat);

    /// <summary>
    /// This box grown by <paramref name="ratio"/> of its width on the west and east and of its height on the south and
    /// north (XD01-429: a viewport fetch reaches past the screen edge, so a short pan reveals features already drawn),
    /// clamped to the world (longitude ±180, latitude ±90). A ratio of 0 clamps only.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ratio"/> is negative or not finite.</exception>
    public BoundingBox Padded(double ratio)
    {
        if (!double.IsFinite(ratio) || ratio < 0)
            throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "The padding ratio must be a finite, non-negative number.");

        var padLon = (MaxLon - MinLon) * ratio;
        var padLat = (MaxLat - MinLat) * ratio;
        return new BoundingBox(
            Math.Max(MinLon - padLon, -180),
            Math.Max(MinLat - padLat, -90),
            Math.Min(MaxLon + padLon, 180),
            Math.Min(MaxLat + padLat, 90));
    }
}
