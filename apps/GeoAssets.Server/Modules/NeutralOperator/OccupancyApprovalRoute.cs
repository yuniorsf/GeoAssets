using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The approval branch of <c>POST /service-orders/{id}/actions</c> (XD01-318). An
/// <see cref="OrderActionType.Approve"/> on an order whose type declares a target policy is not
/// a plain status write: it goes to <see cref="IOccupancyCommitService"/>, which commits the
/// occupancy staged on the order's poles in the same unit as the transition. The answers keep
/// the action route's own dialect, so <c>RestServiceOrderRepository</c> reads them as it reads
/// every other action: 204; 404 for an unknown order or one the caller is outside the
/// organization of; 403 with <c>reason</c>; 400 with <c>from</c>/<c>to</c>; 409 for an order
/// changed meanwhile — plus one problem body, 409 <c>occupancy.concurrent-modification</c> with
/// the poles whose occupancy changed after staging.
/// </summary>
internal static class OccupancyApprovalRoute
{
    /// <summary>True when <paramref name="entry"/> on an order of <paramref name="orderType"/> is an approval that commits staged occupancy.</summary>
    public static bool Handles(OrderActionLog entry, OrderType? orderType) =>
        entry.Action == OrderActionType.Approve && OccupancyApproval.Commits(orderType);

    /// <summary>
    /// Approves through <paramref name="commits"/>. A <c>resultingStatus</c> in the entry, when
    /// given, must be where the approval leads; the entry's performer, instant and comment are
    /// not used — the approval is logged as the caller, at the service's clock.
    /// </summary>
    public static async Task<IResult> ApproveAsync(
        IServiceOrder order, OrderType orderType, OrderActionLog entry, WorkflowPrincipal principal,
        IOccupancyCommitService commits, CancellationToken ct)
    {
        if (entry.ResultingStatus is { } requested
            && !string.Equals(requested, OccupancyApproval.ApprovedStatus(orderType, order.Status), StringComparison.Ordinal))
        {
            return Results.BadRequest(new { from = order.Status, to = requested });
        }

        try
        {
            await commits.ApproveAndCommitAsync(order.Id, principal, ct).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (Exception ex) when (FromException(ex) is { } result)
        {
            return result;
        }
    }

    /// <summary>The answer for what the commit service threw; null for anything that is not a client-facing failure.</summary>
    internal static IResult? FromException(Exception exception) => exception switch
    {
        OrderActionDeniedException { Evaluation: { } verdict } => ServiceOrderRouteAccess.Refusal(verdict),
        KeyNotFoundException => Results.NotFound(),
        InvalidServiceOrderTransitionException transition => Results.BadRequest(new { from = transition.From, to = transition.To }),
        OccupancyConflictException conflict => WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, OccupancyProblemCodes.ConcurrentModification,
            "The occupancy of a pole changed after its changes were staged.", conflict.Message,
            ("poleFeatureIds", conflict.PoleFeatureIds)),
        ServiceOrderConcurrencyException => Results.Conflict(),
        _ => null,
    };
}
