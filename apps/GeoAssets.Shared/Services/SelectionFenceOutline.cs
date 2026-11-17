using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Reads the fence of an order's last selection out of its <see cref="FeatureSelectionSpec"/>
/// so the map can outline it (XD01-311). Only a <c>polygon-fence</c> selection has one; every
/// other strategy yields <see cref="SelectionFenceOutlineKind.None"/>. Never throws: a fence that
/// cannot be drawn is reported as <see cref="SelectionFenceOutlineKind.Malformed"/> for the
/// caller to log.
/// </summary>
public static class SelectionFenceOutline
{
    /// <summary>Fewest positions a ring needs to outline anything.</summary>
    public const int MinRingPositions = 3;

    private static readonly SelectionFenceOutlineResult _none = new(SelectionFenceOutlineKind.None, [], null);

    public static SelectionFenceOutlineResult Read(FeatureSelectionSpec? spec)
    {
        if (spec is null || !string.Equals(spec.StrategyId, PolygonFenceSelectionStrategy.Id, StringComparison.Ordinal))
            return _none;

        if (spec.Parameters is null || !spec.Parameters.ContainsKey(PolygonFenceSelectionStrategy.RingsParameter))
            return Malformed($"the '{PolygonFenceSelectionStrategy.RingsParameter}' parameter is missing");

        double[][][]? rings;
        try
        {
            // Handles both the fresh CLR value and the JsonElement a reloaded order carries.
            rings = spec.Parameters.GetRings(PolygonFenceSelectionStrategy.RingsParameter);
        }
        catch (Exception ex) when (ex is InvalidCastException or InvalidOperationException or FormatException or ArgumentException)
        {
            return Malformed($"the '{PolygonFenceSelectionStrategy.RingsParameter}' parameter is not a list of coordinate rings ({ex.Message})");
        }

        if (rings is null || rings.Length == 0) return Malformed("the fence has no rings");

        for (var r = 0; r < rings.Length; r++)
        {
            var ring = rings[r];
            if (ring is null || ring.Length < MinRingPositions)
                return Malformed($"ring {r} has fewer than {MinRingPositions} positions");

            for (var p = 0; p < ring.Length; p++)
            {
                if (!IsPosition(ring[p]))
                    return Malformed($"ring {r} position {p} is not a longitude/latitude pair within range");
            }
        }

        return new SelectionFenceOutlineResult(SelectionFenceOutlineKind.Drawable, rings, null);
    }

    private static bool IsPosition(double[]? position) =>
        position is [var longitude, var latitude]
        && double.IsFinite(longitude) && double.IsFinite(latitude)
        && longitude is >= -180 and <= 180
        && latitude is >= -90 and <= 90;

    private static SelectionFenceOutlineResult Malformed(string problem) =>
        new(SelectionFenceOutlineKind.Malformed, [], problem);
}
