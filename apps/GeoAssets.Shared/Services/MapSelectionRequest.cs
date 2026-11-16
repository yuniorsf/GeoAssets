using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>
/// A <see cref="MapSelectionGesture"/> expressed as a feature-selection strategy run (XD01-310):
/// the transport-neutral half of an <see cref="IMapSelectionResolver"/> adapter. Both area tools
/// become the <c>polygon-fence</c> strategy; a box is a rectangular fence.
/// </summary>
/// <param name="StrategyId">The selection strategy id.</param>
/// <param name="Parameters">The strategy's parameters.</param>
public sealed record MapSelectionRequest(string StrategyId, IReadOnlyDictionary<string, object> Parameters)
{
    /// <summary>Id of the polygon-fence selection strategy (<c>PolygonFenceSelectionStrategy.Id</c>).</summary>
    public const string PolygonFenceStrategyId = "polygon-fence";

    /// <summary>Name of the strategy parameter carrying the fence's rings.</summary>
    public const string RingsParameter = "rings";

    /// <summary>The fence's rings: closed, <c>[longitude, latitude]</c> positions (RFC 7946).</summary>
    public double[][][] Rings => (double[][][])Parameters[RingsParameter];

    /// <summary>
    /// Normalises and validates <paramref name="gesture"/>. A box becomes the closed five-position
    /// rectangle bounding its positions (so any two opposite corners, in any order, give the same
    /// request); a fence's rings are closed when the drawing left them open. The result is then
    /// checked with <see cref="SelectionFenceValidator"/> — the rules the server applies — so an
    /// unusable area fails here, without a round trip.
    /// </summary>
    /// <exception cref="InvalidSelectionFenceException">The area is unusable (too few or too many
    /// vertices, self-intersecting, out of range, zero area, …).</exception>
    /// <exception cref="ArgumentException">The gesture's tool is not an area tool.</exception>
    public static MapSelectionRequest FromGesture(MapSelectionGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(gesture.Rings);

        double[][][] rings = gesture.Tool switch
        {
            MapSelectionTool.Box => [ToRectangle(gesture.Rings)],
            MapSelectionTool.Fence => [.. gesture.Rings.Select(Close)],
            _ => throw new ArgumentException($"Tool '{gesture.Tool}' does not produce a selection area.", nameof(gesture))
        };

        SelectionFenceValidator.Validate(rings);

        return new MapSelectionRequest(
            PolygonFenceStrategyId,
            new Dictionary<string, object>(StringComparer.Ordinal) { [RingsParameter] = rings });
    }

    /// <summary>The closed rectangle bounding every position of the gesture, counter-clockwise from
    /// the south-west corner.</summary>
    private static double[][] ToRectangle(double[][][] rings)
    {
        var positions = rings.Where(ring => ring is not null).SelectMany(ring => ring).ToList();
        if (positions.Count == 0) throw new InvalidSelectionFenceException(SelectionFenceDefect.Empty);
        if (positions.Any(position => position is not { Length: 2 }))
            throw new InvalidSelectionFenceException(SelectionFenceDefect.MalformedPosition);

        var minLon = positions.Min(position => position[0]);
        var maxLon = positions.Max(position => position[0]);
        var minLat = positions.Min(position => position[1]);
        var maxLat = positions.Max(position => position[1]);

        return [[minLon, minLat], [maxLon, minLat], [maxLon, maxLat], [minLon, maxLat], [minLon, minLat]];
    }

    /// <summary>Appends the first position when the ring does not already end on it. Rings the
    /// validator will reject anyway (null, empty, malformed positions) are passed through.</summary>
    private static double[][] Close(double[][] ring)
    {
        if (ring is not { Length: > 0 } || ring[0] is not { Length: 2 } first || ring[^1] is not { Length: 2 } last)
            return ring;

        return first[0].Equals(last[0]) && first[1].Equals(last[1]) ? ring : [.. ring, [first[0], first[1]]];
    }
}
