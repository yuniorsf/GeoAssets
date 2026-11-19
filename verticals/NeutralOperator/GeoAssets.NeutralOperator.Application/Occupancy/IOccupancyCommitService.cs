using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>
/// Approves an order and commits the occupancy changes staged on its targets into the poles'
/// live occupancy, as one unit (XD01-318).
/// </summary>
public interface IOccupancyCommitService
{
    /// <summary>
    /// Approves <paramref name="orderId"/> on behalf of <paramref name="principal"/>: every staged
    /// set on a target still to be worked is applied to its pole, the sets are marked committed,
    /// sets on skipped or not-executable targets are discarded, the order moves along its
    /// <see cref="Orders.OrderAction.Approve"/> transition and the approval is logged, and
    /// one asset mutation batch refreshing the written poles' occupancy attributes is staged on
    /// the outbox. All of it is stored, or none of it.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="principal"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">The order does not exist, or its type is not registered.</exception>
    /// <exception cref="OrderActionDeniedException">
    /// The rules refuse <see cref="Orders.OrderAction.Approve"/> (a caller outside the
    /// organization gate included), or the caller is not of the order's organization: only the
    /// pole owner commits its occupancy.
    /// </exception>
    /// <exception cref="InvalidOperationException">The order's type declares no target policy.</exception>
    /// <exception cref="Orders.InvalidServiceOrderTransitionException">The order's status has no approval transition.</exception>
    /// <exception cref="Core.Isp.NeutralOperator.Occupancy.OccupancyConflictException">
    /// A pole's occupancy is no longer at the version a set was staged against, or a set is no
    /// longer staged: nothing was committed.
    /// </exception>
    /// <exception cref="Orders.ServiceOrderConcurrencyException">The order changed while it was being approved: nothing was committed.</exception>
    Task<OccupancyCommitResult> ApproveAndCommitAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct);
}
