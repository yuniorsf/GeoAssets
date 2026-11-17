using GeoAssets.Shared.Interfaces;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// How a target of the shown service order looks on the map (XD01-313), and the legend that
/// explains it. One state per target, decided in this order:
/// <list type="number">
/// <item><description>Skipped or not executable — no work will be done on it: <see cref="MapFeatureState.Disabled"/>.</description></item>
/// <item><description>Blocked by another open order — it cannot be worked: <see cref="MapFeatureState.Disabled"/>.</description></item>
/// <item><description>Done: <see cref="MapFeatureState.Done"/>.</description></item>
/// <item><description>Overlapping another open order (a warning): <see cref="MapFeatureState.Conflict"/>.</description></item>
/// <item><description>Anything else — pending, in progress, or a status this client does not know: <see cref="MapFeatureState.Pending"/>.</description></item>
/// </list>
/// So a conflict (danger colour) and a blocked target (dimmed) never look alike.
/// </summary>
public static class OrderTargetMapState
{
    /// <summary>The states a target can take, in the order the legend lists them.</summary>
    public static IReadOnlyList<MapFeatureState> Legend { get; } =
        [MapFeatureState.Pending, MapFeatureState.Done, MapFeatureState.Conflict, MapFeatureState.Disabled];

    /// <summary>The map state of <paramref name="target"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    public static MapFeatureState Of(ServiceOrderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Status is OrderTargetStatus.Skipped or OrderTargetStatus.NotExecutable) return MapFeatureState.Disabled;
        if (target.ConflictLevel == OrderTargetConflictLevel.Blocked) return MapFeatureState.Disabled;
        if (string.Equals(target.Status, OrderTargetStatus.Done, StringComparison.Ordinal)) return MapFeatureState.Done;

        return target.ConflictLevel == OrderTargetConflictLevel.Warning ? MapFeatureState.Conflict : MapFeatureState.Pending;
    }

    /// <summary>The legend text key of a target state; any other state has none.</summary>
    public static string? LegendKey(MapFeatureState state) => state switch
    {
        MapFeatureState.Pending => "targetWork.map.pending",
        MapFeatureState.Done => "targetWork.map.done",
        MapFeatureState.Conflict => "targetWork.map.conflict",
        MapFeatureState.Disabled => "targetWork.map.disabled",
        _ => null,
    };

    /// <summary>The CSS class of a legend swatch, coloured with the state's map token.</summary>
    public static string SwatchClass(MapFeatureState state) => "order-target-map-swatch order-target-map-swatch-" + state.ToJsName();
}
