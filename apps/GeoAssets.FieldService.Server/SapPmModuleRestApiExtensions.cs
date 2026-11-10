using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Erp.Sap;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// SAP PM/S4HANA integration hub (XD01-231): the inbound work-order-notification webhook that
/// spawns a <see cref="ServiceOrderStatus.Pending"/> order via <see cref="SapPmOrderSpawnService"/>,
/// and the order-confirmation endpoint that drives <see cref="SapPmOrderConfirmationDispatchService"/>
/// on the <see cref="FieldWorkStatus.Inspected"/> transition. Same "cross-tenant id looks like
/// 404" convention as <c>FtthDropInstallRestApiExtensions</c>.
/// </summary>
internal static class SapPmModuleRestApiExtensions
{
    public static IEndpointRouteBuilder MapSapPmApi(
        this IEndpointRouteBuilder routes, string prefix = "/api/field-service/sap-pm")
    {
        routes.MapPost($"{prefix}/webhooks/work-order-notification", IngestWorkOrderNotificationAsync)
            .WithName("IngestSapPmWorkOrderNotification")
            .WithSummary("Webhook ingestion endpoint for SAP PM work-order notifications — maps FunctionalLocation to a geo_entity, spawns a Pending order with its SLA deadline attached, deduplicated on NotificationId.")
            .Produces<SapPmWebhookAckResponse>(StatusCodes.Status202Accepted)
            .Produces<SapPmWebhookAckResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        routes.MapPost($"{prefix}/orders/{{orderId}}/confirm", ConfirmAsync)
            .WithName("ConfirmSapPmOrder")
            .WithSummary("Transitions a SAP PM-spawned order to Inspected and sends exactly one outbound confirmation to SAP.")
            .Produces<SapPmConfirmationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> IngestWorkOrderNotificationAsync(
        SapPmWebhookIngestRequest request, ISapPmOrderAdapter adapter, SapPmOrderSpawnService spawnService,
        ITenantContext tenantContext, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            return Results.BadRequest("PayloadJson is required.");

        try
        {
            var spec = adapter.ParseWorkOrderNotification(request.PayloadJson);
            var result = await spawnService
                .SpawnFromNotificationAsync(spec, FieldWorkOrderTypes.FieldWorkId, tenantContext.CurrentOrganizationId, ct)
                .ConfigureAwait(false);

            if (result.AlreadyProcessed)
                return Results.Accepted(value: new SapPmWebhookAckResponse("AlreadyProcessed", null));

            return Results.Created(
                new Uri($"/api/field-service/orders/{result.Order!.Id}", UriKind.Relative),
                new SapPmWebhookAckResponse("Accepted", result.Order.Id));
        }
        catch (SapPmOrderParseException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (SapPmFunctionalLocationNotFoundException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> ConfirmAsync(
        string orderId, SapPmConfirmationRequest request, IServiceOrderRepository serviceOrders,
        OrderTypeRegistry orderTypeRegistry, SapPmOrderConfirmationDispatchService dispatchService,
        ITenantContext tenantContext, CancellationToken ct)
    {
        var existing = await serviceOrders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        if (existing is null || existing.OrganizationId != tenantContext.CurrentOrganizationId)
            return Results.NotFound();

        var fieldWorkOrderType = orderTypeRegistry.Find(FieldWorkOrderTypes.FieldWorkId)
            ?? throw new InvalidOperationException($"OrderType '{FieldWorkOrderTypes.FieldWorkId}' is not registered.");

        try
        {
            var result = await dispatchService
                .TransitionToInspectedAsync(orderId, request.LaborHours, request.Materials, request.EvidenceDossierUrl, fieldWorkOrderType, ct)
                .ConfigureAwait(false);

            return Results.Ok(SapPmConfirmationResponse.FromResult(result));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidServiceOrderTransitionException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
}
