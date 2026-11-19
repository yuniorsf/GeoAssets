using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>
/// Which approvals commit staged occupancy (XD01-318), and where they lead. An order whose type
/// declares the <see cref="OccupancyCapabilities.Commit"/> capability (XD01-361) and a target policy
/// holds its occupancy work per target: approving it goes through <see cref="IOccupancyCommitService"/>,
/// never through a plain status write that would close the order and discard what was staged. Any
/// other targeted type approves through the default path.
/// </summary>
public static class OccupancyApproval
{
    /// <summary>
    /// True when approving orders of <paramref name="orderType"/> commits their staged occupancy: the type declares the
    /// <see cref="OccupancyCapabilities.Commit"/> capability and has targets to commit on.
    /// </summary>
    public static bool Commits(OrderType? orderType) =>
        orderType is { TargetPolicy: not null } && orderType.HasCapability(OccupancyCapabilities.Commit);

    /// <summary>
    /// The status an approval moves an order of <paramref name="orderType"/> in
    /// <paramref name="status"/> to: the target of the type's transition tagged
    /// <see cref="OrderAction.Approve"/> out of <paramref name="status"/>; null when the type
    /// has no such transition (or no graph of its own).
    /// </summary>
    public static string? ApprovedStatus(OrderType? orderType, string status) =>
        orderType is { States.Count: > 0 }
            ? orderType.Transitions.FirstOrDefault(t =>
                t.TriggerAction == OrderAction.Approve
                && string.Equals(t.FromStateKey, status, StringComparison.Ordinal))?.ToStateKey
            : null;
}
