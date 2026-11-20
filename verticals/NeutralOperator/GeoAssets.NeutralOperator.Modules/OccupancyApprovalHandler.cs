using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.Modules;

/// <summary>
/// The approval branch of <c>POST /service-orders/{id}/actions</c> (XD01-318), as the NeutralOperator module's
/// <see cref="IOrderActionHandler"/> (XD01-360; it was a special case inside the generic route). An
/// <see cref="OrderAction.Approve"/> on an order whose type declares a target policy is not a plain status write: it goes
/// to <see cref="IOccupancyCommitService"/>, which commits the occupancy staged on the order's poles in the same unit as the
/// transition. The outcomes keep the action route's own dialect — applied, not found, denied, invalid transition, changed
/// meanwhile — plus two problems, 409 <c>occupancy.concurrent-modification</c> with the poles whose occupancy changed after
/// staging, and 409 <c>occupancy.apply-pending</c> (XD01-383) when an earlier approval of the order — or of another order
/// holding one of its poles — still awaits the asset catalog's apply. Applied means approved: when poles were written the
/// order is "approved, awaiting apply" until the kernel answers the approval saga.
/// </summary>
public sealed class OccupancyApprovalHandler(IOccupancyCommitService commits) : IOrderActionHandler
{
    public bool CanHandle(OrderType? orderType, OrderActionLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Action == OrderAction.Approve && OccupancyApproval.Commits(orderType);
    }

    /// <summary>
    /// Approves through the commit service. A <c>resultingStatus</c> in the entry, when given, must be where the approval
    /// leads; the entry's performer, instant and comment are not used — the approval is logged as the caller, at the
    /// service's clock.
    /// </summary>
    public async Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (order, orderType, entry, principal) = context;
        if (entry.ResultingStatus is { } requested
            && !string.Equals(requested, OccupancyApproval.ApprovedStatus(orderType, order.Status), StringComparison.Ordinal))
        {
            return OrderActionOutcome.InvalidTransition(order.Status, requested);
        }

        try
        {
            await commits.ApproveAndCommitAsync(order.Id, principal, cancellationToken).ConfigureAwait(false);
            return OrderActionOutcome.Applied;
        }
        catch (Exception ex) when (FromException(ex) is { } outcome)
        {
            return outcome;
        }
    }

    /// <summary>The outcome for what the commit service threw; null for anything that is not a client-facing failure.</summary>
    internal static OrderActionOutcome? FromException(Exception exception) => exception switch
    {
        OrderActionDeniedException { Evaluation: { } verdict } => OrderActionOutcome.Denied(verdict),
        KeyNotFoundException => OrderActionOutcome.NotFound,
        InvalidServiceOrderTransitionException transition => OrderActionOutcome.InvalidTransition(transition.From, transition.To),
        OccupancyConflictException conflict => OrderActionOutcome.Problem(
            StatusCodes.Status409Conflict, OccupancyProblemCodes.ConcurrentModification,
            "The occupancy of a pole changed after its changes were staged.", conflict.Message,
            ("poleFeatureIds", conflict.PoleFeatureIds)),
        OccupancyApplyPendingException pending => OrderActionOutcome.Problem(
            StatusCodes.Status409Conflict, OccupancyProblemCodes.ApplyPending,
            "An earlier approval awaits the asset catalog's apply.", pending.Message,
            ("poleFeatureIds", pending.PoleFeatureIds)),
        ServiceOrderConcurrencyException => OrderActionOutcome.ConcurrentModification,
        _ => null,
    };
}
