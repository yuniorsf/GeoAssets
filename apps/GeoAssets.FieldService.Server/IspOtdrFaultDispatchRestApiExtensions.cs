using System.Diagnostics;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.FieldWork;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// OTDR .SOR trace ingestion & emergency fault dispatch (XD01-283), built on XD01-282's
/// <see cref="IOtdrFaultCorrelationEngine"/> and XD01-279's ISP vertical host scaffolding.
///
/// Also records two of XD01-287's ISP rollout KPIs: the utility-cut/outage dispatch count
/// (<see cref="GeoAssetsMeter.RecordUtilityCutOutageDispatched"/>) and the fault-dispatch
/// processing latency (<see cref="GeoAssetsMeter.RecordFaultDispatchLatency"/>) — a lower bound
/// on real-world MTTR, not true detection-to-repair MTTR: nothing in this codebase tracks a
/// fault-resolution timestamp today, so the honestly-supportable proxy is how long correlation
/// itself takes, not the full field-repair lifecycle.
/// </summary>
internal static class IspOtdrFaultDispatchRestApiExtensions
{
    public static IEndpointRouteBuilder MapIspOtdrFaultDispatchApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp/otdr")
    {
        routes.MapPost($"{prefix}/fault-dispatch", DispatchAsync)
            .WithName("DispatchOtdrFault")
            .WithSummary("Parses an uploaded .SOR OTDR trace (or a direct OLT LOS-alarm optical distance), correlates it against the cable's stored geometry, and returns the pinpointed fault coordinate, bounding pole pair, and diagnostic summary.")
            .Produces<OtdrFaultCorrelationResult>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> DispatchAsync(
        IspOtdrFaultDispatchRequest request,
        IGeoCoreClient geoCoreClient,
        OtdrEmergencyFaultDispatchService dispatchService,
        GeoAssetsMeter metrics,
        CancellationToken ct)
    {
        var cableFeature = await geoCoreClient.GetFeatureByIdAsync(request.CableFeatureId, ct).ConfigureAwait(false);
        if (cableFeature is null)
            return Results.NotFound();

        var source = new OtdrFaultSourceInput(request.SorTraceBytes, request.DirectOpticalDistanceMeters);

        var stopwatch = Stopwatch.StartNew();
        var outcome = dispatchService.Dispatch(
            source,
            cableFeature,
            request.HelixFactor,
            request.CentralOfficeSlackMeters,
            request.SlackPoints,
            request.PolesAlongRoute,
            request.SpliceMatchToleranceMeters);
        stopwatch.Stop();

        if (!outcome.Succeeded)
            return Results.BadRequest(outcome.FailureReason);

        metrics.RecordFaultDispatchLatency(stopwatch.Elapsed.TotalMilliseconds);
        metrics.RecordUtilityCutOutageDispatched();
        return Results.Ok(outcome.Result);
    }
}
