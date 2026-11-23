using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>
/// Turns the bounds Leaflet reports for the view into a <c>bbox</c> the reference-features route accepts (XD01-330:
/// finite, longitudes within ±180, latitudes within ±90, min ≤ max — anything else is a <c>400</c>).
/// </summary>
public static class ReferenceViewport
{
    private const double FullTurn = 360;

    /// <summary>
    /// Latitudes are clamped to ±90. A view at least a full turn wide asks for every longitude. Otherwise the view is
    /// moved by whole turns so its west edge lies in [-180, 180) — a pan onto a wrapped copy of the world asks for the
    /// longitudes it shows — and a view that then crosses the antimeridian asks for every longitude too, rather than
    /// splitting it into two requests (over-fetching, never missing a feature; the server caps the answer). Null when a
    /// bound is not finite or a minimum exceeds its maximum: there is nothing sensible to ask for.
    /// </summary>
    public static BoundingBox? Normalize(double minLon, double minLat, double maxLon, double maxLat)
    {
        if (!double.IsFinite(minLon) || !double.IsFinite(minLat) || !double.IsFinite(maxLon) || !double.IsFinite(maxLat))
            return null;
        if (minLon > maxLon || minLat > maxLat) return null;

        var south = Math.Clamp(minLat, -90, 90);
        var north = Math.Clamp(maxLat, -90, 90);

        if (maxLon - minLon >= FullTurn) return new BoundingBox(-180, south, 180, north);

        var shift = Math.Floor((minLon + 180) / FullTurn) * FullTurn;
        var west = minLon - shift;
        var east = maxLon - shift;

        return east > 180
            ? new BoundingBox(-180, south, 180, north)
            : new BoundingBox(west, south, east, north);
    }
}
