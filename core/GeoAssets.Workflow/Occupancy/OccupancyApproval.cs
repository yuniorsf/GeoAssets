using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// Which approvals commit staged occupancy (XD01-318), and where they lead. An order whose type
/// declares a target policy holds its work per target, and so may hold staged occupancy changes:
/// approving it goes through <see cref="IOccupancyCommitService"/>, never through a plain status
/// write that would close the order and discard what was staged.
/// </summary>
public static class OccupancyApproval
{
    /// <summary>True when approving orders of <paramref name="orderType"/> commits their staged occupancy: the type declares a target policy.</summary>
    public static bool Commits(OrderType? orderType) => orderType?.TargetPolicy is not null;

    /// <summary>
    /// The status an approval moves an order of <paramref name="orderType"/> in
    /// <paramref name="status"/> to: the target of the type's transition tagged
    /// <see cref="OrderActionType.Approve"/> out of <paramref name="status"/>; null when the type
    /// has no such transition (or no graph of its own).
    /// </summary>
    public static string? ApprovedStatus(OrderType? orderType, string status) =>
        orderType is { States.Count: > 0 }
            ? orderType.Transitions.FirstOrDefault(t =>
                t.TriggerAction == OrderActionType.Approve
                && string.Equals(t.FromStateKey, status, StringComparison.Ordinal))?.ToStateKey
            : null;
}
