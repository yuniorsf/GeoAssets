using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.FieldWork;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// OTDR .SOR trace ingestion & emergency fault dispatch (XD01-283), built on XD01-282's
/// <see cref="IOtdrFaultCorrelationEngine"/> and XD01-279's ISP vertical host scaffolding.
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
        CancellationToken ct)
    {
        var cableFeature = await geoCoreClient.GetFeatureByIdAsync(request.CableFeatureId, ct).ConfigureAwait(false);
        if (cableFeature is null)
            return Results.NotFound();

        var source = new OtdrFaultSourceInput(request.SorTraceBytes, request.DirectOpticalDistanceMeters);
        var outcome = dispatchService.Dispatch(
            source,
            cableFeature,
            request.HelixFactor,
            request.CentralOfficeSlackMeters,
            request.SlackPoints,
            request.PolesAlongRoute,
            request.SpliceMatchToleranceMeters);

        return outcome.Succeeded ? Results.Ok(outcome.Result) : Results.BadRequest(outcome.FailureReason);
    }
}
