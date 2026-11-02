using GeoAssets.Core.Interfaces;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Isp.Bss;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Drives a FTTH drop-install order through <see cref="FtthDropInstallWorkflowGraph"/> (XD01-273,
/// never host-wired before XD01-280) via <see cref="FtthDropInstallActivationDispatchService"/>,
/// which also notifies the spawning BSS/OSS provider on activation (XD01-280's AC2). Same
/// "cross-tenant id looks like 404" convention as <c>FieldWorkInspectionRestApiExtensions</c>.
///
/// Also where two of XD01-287's ISP rollout KPIs are recorded: drop-install elapsed time
/// (<see cref="GeoAssetsMeter.RecordFtthDropInstallDuration"/>, once the transition reaches
/// <see cref="FtthDropInstallStatus.Activated"/>) and the optical-budget verification outcome
/// (<see cref="GeoAssetsMeter.RecordOpticalBudgetOutcome"/>, observed on the
/// TagAffixed→PtoPower edge) — this is the request boundary, not
/// <see cref="FtthDropInstallActivationDispatchService"/> itself, because
/// <c>core/GeoAssets.Workflow</c> deliberately carries no reference to
/// <c>GeoAssets.Infrastructure.Observability</c> (unlike its EFCore/messaging/host siblings) and
/// this ticket isn't introducing that as the first one.
/// </summary>
internal static class FtthDropInstallRestApiExtensions
{
    public static IEndpointRouteBuilder MapFtthDropInstallApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp/ftth-drop-install")
    {
        routes.MapPost($"{prefix}/{{orderId}}/transition", TransitionAsync)
            .WithName("TransitionFtthDropInstallOrder")
            .WithSummary("Advances a FTTH drop-install order to a new status; notifies the spawning BSS/OSS provider when the order reaches Activated.")
            .Produces<FtthDropInstallTransitionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> TransitionAsync(
        string orderId, FtthDropInstallTransitionRequest request, IServiceOrderRepository serviceOrders,
        OrderTypeRegistry orderTypeRegistry, FtthDropInstallActivationDispatchService dispatchService,
        ITenantContext tenantContext, TimeProvider timeProvider, GeoAssetsMeter metrics, CancellationToken ct)
    {
        var existing = await serviceOrders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        if (existing is null || existing.OrganizationId != tenantContext.CurrentOrganizationId)
            return Results.NotFound();

        var ftthOrderType = orderTypeRegistry.Find(FtthDropInstallOrderTypes.FtthDropInstallId)
            ?? throw new InvalidOperationException($"OrderType '{FtthDropInstallOrderTypes.FtthDropInstallId}' is not registered.");

        var enteringPtoPower =
            string.Equals(existing.Status, FtthDropInstallStatus.TagAffixed, StringComparison.Ordinal) &&
            string.Equals(request.NewStatus, FtthDropInstallStatus.PtoPower, StringComparison.Ordinal);

        try
        {
            var result = await dispatchService.TransitionAsync(orderId, request.NewStatus, ftthOrderType, ct).ConfigureAwait(false);

            if (enteringPtoPower)
                metrics.RecordOpticalBudgetOutcome("Passed");

            if (string.Equals(request.NewStatus, FtthDropInstallStatus.Activated, StringComparison.Ordinal))
                metrics.RecordFtthDropInstallDuration((timeProvider.GetUtcNow().UtcDateTime - existing.CreatedAt).TotalSeconds);

            return Results.Ok(FtthDropInstallTransitionResponse.FromResult(result));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidServiceOrderTransitionException ex)
        {
            if (enteringPtoPower)
                metrics.RecordOpticalBudgetOutcome("GuardBlocked");

            return Results.BadRequest(ex.Message);
        }
    }
}
