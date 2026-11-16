using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// Stages and discards proposed occupancy changes on the targets of a service order, and reads
/// a target's pole as it is and as it would become (XD01-315). Nothing here writes a pole's
/// live occupancy: that happens only when the order is approved.
///
/// <para><b>Every member checks, in this order:</b></para>
/// <list type="number">
///   <item><description>a null argument → <see cref="ArgumentNullException"/>; a blank id →
///   <see cref="ArgumentException"/>; paging out of range → <see cref="ArgumentOutOfRangeException"/>;</description></item>
///   <item><description>an unknown order → <see cref="KeyNotFoundException"/>;</description></item>
///   <item><description>the order rules — <see cref="Orders.OrderActionType.View"/> to read,
///   <see cref="Orders.OrderActionType.Annotate"/> to stage or discard — refuse the principal →
///   <see cref="OrderActionDeniedException"/> carrying the verdict (a principal outside the
///   order's organization gets the verdict a host reports as "no such order");</description></item>
///   <item><description><b>owner only:</b> a principal who passed the rules without being a
///   member of the order's organization — a cross-organization grantee, a dispatch recipient —
///   → <see cref="OrderActionDeniedException"/>. Occupancy names every occupant of the pole
///   owner's poles; it is neither shown to nor edited by another organization.</description></item>
/// </list>
/// <para>Members addressing one target then require the target to belong to the order
/// (<see cref="OrderTargetNotFoundException"/>) and to be a pole
/// (<see cref="TargetIsNotAPoleException"/>).</para>
/// </summary>
public interface IOrderOccupancyStagingService
{
    /// <summary>
    /// The live occupancy of the target's pole, the set staged on the target (if any), the
    /// preview of the pole with those changes applied, and whether the pole moved since staging.
    /// </summary>
    Task<TargetOccupancyView> GetAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>
    /// One page of the order's change sets — those in <paramref name="status"/>, or in every
    /// status when null — ordered by pole feature id, then set id. <paramref name="page"/> is
    /// 1-based; <paramref name="pageSize"/> is 1..<see cref="OccupancyChangeSetQuery.MaxPageSize"/>.
    /// </summary>
    Task<PagedResult<OccupancyChangeSetView>> GetPageAsync(
        string orderId, int page, int pageSize, OccupancyChangeSetStatus? status, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>
    /// Stages <paramref name="changes"/> on the target, replacing whatever was staged there, and
    /// returns the target's view afterwards. Safe to repeat. The changes must be valid and apply
    /// to the pole's live occupancy now; the pole's current version becomes the set's baseline —
    /// a pole that moved since an earlier staging is not an error here.
    /// </summary>
    /// <exception cref="Orders.InvalidServiceOrderTransitionException">The order is in a terminal state.</exception>
    /// <exception cref="InvalidOccupancyChangeException">A change is malformed or does not apply to the pole.</exception>
    /// <exception cref="OccupancyEntryNotFoundException">A change addresses an empty slot or an entry not on the pole.</exception>
    /// <exception cref="OccupancyTargetReleasedException">The target was released when its order closed.</exception>
    Task<TargetOccupancyView> StageAsync(
        string orderId, string targetId, IReadOnlyCollection<OccupancyChange> changes, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>
    /// Discards the set staged on the target. Returns true when one was discarded, false when
    /// nothing was staged — repeating the call is harmless, and so is calling it on a closed order.
    /// </summary>
    Task<bool> DiscardAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>Discards every set staged on the order's targets and returns how many.</summary>
    Task<int> DiscardAllAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct);
}
