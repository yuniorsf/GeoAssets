using GeoAssets.Integrations.Scada;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the OMS-facing SCADA breaker-trip ingest endpoint (XD01-239) — an external OMS posts
/// here when a breaker trips; this host resolves the isolated feeder segment and auto-creates
/// the emergency dispatch order via <see cref="IFeederOutageDispatchService"/>.
/// </summary>
internal static class ScadaTopologyTraverseRestApiExtensions
{
    public static IEndpointRouteBuilder MapScadaTopologyTraverseApi(
        this IEndpointRouteBuilder routes,
        string route = "/api/geoassets/topology/traverse")
    {
        routes.MapPost(route, async (
            ScadaTopologyTraverseRequest request,
            IFeederOutageDispatchService dispatchService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.TrippedSwitchFeatureId))
                return Results.BadRequest("trippedSwitchFeatureId is required.");

            FeederOutageDispatchResult result;
            try
            {
                result = await dispatchService.DispatchForBreakerTripAsync(request.TrippedSwitchFeatureId, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound($"Switch feature '{request.TrippedSwitchFeatureId}' not found.");
            }

            // No order could be created — the tripped switch itself couldn't be read from the core
            // (XD01-386). 503 so the OMS retries the alarm instead of treating it as handled.
            if (result.ServiceOrderId is null)
                return GeoCoreUnavailableProblem.For(result.DegradedReason ?? "The tripped switch could not be read from the core.");

            return Results.Ok(new ScadaTopologyTraverseResponse(
                result.ServiceOrderId,
                result.ImpactedFeatureIds,
                result.CriticalFacilityFeatureIds,
                result.DispatchedCrewFeatureId,
                result.IsDegraded,
                result.DegradedReason));
        })
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .WithName("ScadaTopologyTraverse")
        .WithSummary("Consumes a SCADA breaker-trip alarm: resolves the isolated feeder segment and auto-dispatches an emergency ServiceOrder.");

        return routes;
    }
}
