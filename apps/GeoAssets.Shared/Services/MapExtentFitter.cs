using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Turns an order's target extent into the bounding box handed to
/// <see cref="Interfaces.IMapInterop.FitBoundsAsync"/> (XD01-311, MAP-B24). Pure and static so
/// the decision is tested without a map.
/// </summary>
public static class MapExtentFitter
{
    /// <summary>
    /// Smallest width and height, in degrees, of a box the map is asked to fit (about 1.1 km of
    /// latitude). A single target, or targets on one meridian or parallel, give a box with no
    /// width and/or height; fitting that as-is would zoom all the way in. Growing it to this
    /// span keeps the fit at a neighbourhood scale (around zoom 16–17 on a desktop-sized map).
    /// </summary>
    public const double MinSpanDegrees = 0.01;

    private const double MaxLongitude = 180;
    private const double MaxLatitude = 90;

    /// <summary>
    /// Returns <c>[minLon, minLat, maxLon, maxLat]</c> (RFC 7946 order), or <c>null</c> when
    /// there is nothing to fit: a null extent, or one with a NaN or infinite coordinate.
    /// <list type="bullet">
    /// <item><description>Corners given the wrong way round are put in order.</description></item>
    /// <item><description>Coordinates are clamped to [-180, 180] and [-90, 90].</description></item>
    /// <item><description>A side shorter than <see cref="MinSpanDegrees"/> is grown to it around
    /// its middle, and shifted back inside the valid range when that would cross the edge of
    /// the world — so the result never leaves [-180, 180] × [-90, 90] and never wraps the
    /// antimeridian.</description></item>
    /// </list>
    /// </summary>
    public static double[]? ToFitBounds(OrderTargetExtent? extent)
    {
        if (extent is null) return null;
        if (!double.IsFinite(extent.MinLongitude) || !double.IsFinite(extent.MinLatitude)
            || !double.IsFinite(extent.MaxLongitude) || !double.IsFinite(extent.MaxLatitude))
            return null;

        var (minLon, maxLon) = Span(extent.MinLongitude, extent.MaxLongitude, MaxLongitude);
        var (minLat, maxLat) = Span(extent.MinLatitude, extent.MaxLatitude, MaxLatitude);
        return [minLon, minLat, maxLon, maxLat];
    }

    private static (double Min, double Max) Span(double a, double b, double limit)
    {
        var min = Math.Clamp(Math.Min(a, b), -limit, limit);
        var max = Math.Clamp(Math.Max(a, b), -limit, limit);
        if (max - min >= MinSpanDegrees) return (min, max);

        var middle = (min + max) / 2;
        min = middle - MinSpanDegrees / 2;
        max = middle + MinSpanDegrees / 2;
        if (min < -limit) return (-limit, -limit + MinSpanDegrees);
        if (max > limit) return (limit - MinSpanDegrees, limit);
        return (min, max);
    }
}
