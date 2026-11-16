using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Validates a user-drawn selection fence (RFC 7946 polygon coordinates: rings of
/// [longitude, latitude] positions, exterior ring first) and turns it into a <see cref="GeoPolygon"/>.
///
/// <para>The fence is accepted or rejected as given: a ring is never closed on the caller's behalf
/// and an invalid polygon is never repaired. Either winding order is accepted and the coordinates
/// are returned unchanged. Checks run in the order of <see cref="SelectionFenceDefect"/>'s members,
/// each over the whole fence before the next starts, so the reported defect is deterministic.</para>
/// </summary>
public static class SelectionFenceValidator
{
    /// <summary>Default ceiling on the total number of vertices over all rings.</summary>
    public const int DefaultMaxVertices = 2000;

    private const int MinRingPositions = 4;
    private const double MaxLongitudeSpanDegrees = 180d;

    /// <exception cref="ArgumentNullException"><paramref name="rings"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidSelectionFenceException">The fence is rejected; see <see cref="InvalidSelectionFenceException.Defect"/>.</exception>
    public static GeoPolygon Validate(double[][][] rings, int maxVertices = DefaultMaxVertices)
    {
        ArgumentNullException.ThrowIfNull(rings);

        if (rings.Length == 0)
            throw new InvalidSelectionFenceException(SelectionFenceDefect.Empty);

        if (rings.Any(ring => ring is null || ring.Any(position => position is not { Length: 2 })))
            throw new InvalidSelectionFenceException(SelectionFenceDefect.MalformedPosition);

        if (rings.Any(ring => ring.Any(p => !double.IsFinite(p[0]) || !double.IsFinite(p[1]))))
            throw new InvalidSelectionFenceException(SelectionFenceDefect.NonFiniteCoordinate);

        if (rings.Any(ring => ring.Any(p => p[0] is < -180d or > 180d || p[1] is < -90d or > 90d)))
            throw new InvalidSelectionFenceException(SelectionFenceDefect.OutOfRange);

        if (rings.Any(ring => ring.Length < MinRingPositions))
            throw new InvalidSelectionFenceException(SelectionFenceDefect.TooFewVertices);

        if (rings.Any(ring => !IsSamePosition(ring[0], ring[^1])))
            throw new InvalidSelectionFenceException(SelectionFenceDefect.NotClosed);

        if (rings.Sum(ring => (long)ring.Length) > maxVertices)
            throw new InvalidSelectionFenceException(SelectionFenceDefect.TooManyVertices);

        var exterior = rings[0];
        if (exterior.Max(p => p[0]) - exterior.Min(p => p[0]) > MaxLongitudeSpanDegrees)
            throw new InvalidSelectionFenceException(SelectionFenceDefect.CrossesAntimeridian);

        var polygon = new GeoPolygon { Coordinates = rings };

        if (!polygon.IsValid)
            throw new InvalidSelectionFenceException(SelectionFenceDefect.SelfIntersecting);

        if (polygon.IsEmpty || polygon.Area == 0d)
            throw new InvalidSelectionFenceException(SelectionFenceDefect.ZeroArea);

        return polygon;
    }

    private static bool IsSamePosition(double[] first, double[] last) =>
        first[0].Equals(last[0]) && first[1].Equals(last[1]);
}
