using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server;

/// <summary>
/// The HTTP answer of the generic action route for an <see cref="OrderActionOutcome"/> (XD01-360) — the same dialect
/// <c>RestServiceOrderRepository</c> reads for every action: 204; 404 for an unknown order or one outside the caller's
/// organization; 403 with <c>reason</c>; 400 with <c>from</c>/<c>to</c>; 409 for an order changed meanwhile; and a handler's
/// own problem body.
/// </summary>
public static class OrderActionResults
{
    public static IResult ToResult(OrderActionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Kind switch
        {
            OrderActionOutcomeKind.Applied => Results.NoContent(),
            OrderActionOutcomeKind.NotFound => Results.NotFound(),
            OrderActionOutcomeKind.ConcurrentModification => Results.Conflict(),
            OrderActionOutcomeKind.Denied => ServiceOrderRouteAccess.Refusal(outcome.Verdict!),
            OrderActionOutcomeKind.InvalidTransition => Results.BadRequest(new { from = outcome.From, to = outcome.To }),
            OrderActionOutcomeKind.Problem => WorkflowProblemResults.Create(
                outcome.StatusCode, outcome.Code!, outcome.Title!, outcome.Detail, [.. outcome.Extensions]),
            _ => throw new InvalidOperationException($"Unknown order action outcome kind '{outcome.Kind}'."),
        };
    }
}
