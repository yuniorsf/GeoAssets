using GeoAssets.Workflow.Agents.Dispatch;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the read-only advisory-plan endpoint (XD01-256) a dispatcher UI polls to preview the
/// day-to-day yield-ranked VRPTW dispatch suggestion. Deliberately a GET with no corresponding
/// "commit this plan" action here — turning a suggestion into a real dispatch stays whatever
/// existing human-driven or emergency-agent order-dispatch path already does it, per this
/// ticket's "advisory/overridable... not auto-committed without review" AC.
/// </summary>
internal static class DynamicDispatchAdvisoryRestApiExtensions
{
    public static IEndpointRouteBuilder MapDynamicDispatchAdvisoryApi(
        this IEndpointRouteBuilder routes,
        string route = "/api/geoassets/dispatch/advisory-plan")
    {
        routes.MapGet(route, async (
            DynamicCrewDispatchAdvisorService advisor,
            CancellationToken ct) =>
        {
            var result = await advisor.GenerateAdvisoryPlanAsync(ct: ct).ConfigureAwait(false);

            return Results.Ok(new DynamicDispatchAdvisoryResponse(
                [.. result.SuggestedItineraries.Select(i => new DynamicDispatchItineraryDto(
                    i.CrewFeatureId,
                    [.. i.Stops.Select(s => new DynamicDispatchStopDto(s.OrderId, s.SequenceIndex, s.EstimatedArrival))],
                    i.TotalDistanceMeters))],
                result.RankedOrderIds,
                result.DeadlineCriticalOrderIds,
                result.UnassignedOrderIds,
                result.SkippedNoLocationOrderIds,
                result.OptimizedDistanceMeters,
                result.BaselineDistanceMeters));
        })
        .WithName("DynamicDispatchAdvisoryPlan")
        .WithSummary("Previews a yield-ranked, VRPTW-routed dispatch suggestion for open service orders — advisory only, never auto-dispatches.");

        return routes;
    }
}
