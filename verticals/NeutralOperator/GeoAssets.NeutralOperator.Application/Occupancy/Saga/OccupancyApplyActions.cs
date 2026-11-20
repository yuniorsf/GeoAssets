using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// The NeutralOperator vertical's own action-log verbs for the approval saga (XD01-383; the open vocabulary of XD01-366):
/// written by the saga, never requested through the action route.
/// </summary>
public static class OccupancyApplyActions
{
    /// <summary>The kernel applied the approval's batch; the order moved to its approved status.</summary>
    public static OrderAction Applied { get; } = new("OccupancyApplied");

    /// <summary>The kernel rejected the approval's batch; the occupancy was restored and the reason recorded.</summary>
    public static OrderAction Reverted { get; } = new("OccupancyApplyReverted");

    /// <summary>An operator re-staged the approval's batch.</summary>
    public static OrderAction Retried { get; } = new("OccupancyApplyRetried");
}
