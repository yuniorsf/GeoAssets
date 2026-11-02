using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Isp.Bss;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// ISP vertical host scaffolding (XD01-279): a webhook ingestion endpoint and a thin
/// <see cref="IGeoCoreClient"/> proxy, mirroring the Electrical/Sanitation/NeutralPole archetype
/// modules' own <c>MapXModuleApi</c> shape (<see cref="ElectricalModuleRestApiExtensions"/> et
/// al.). The ingest endpoint now dispatches real order-spawn logic for
/// <c>EventType == "order.spawn"</c> (XD01-280) when <c>ProviderId</c> resolves to a registered
/// <see cref="IBssOssOrderAdapter"/>; every other envelope (including every event type XD01-279's
/// own tests already cover) keeps the original shell-acceptance behavior unchanged, so adding real
/// dispatch here never breaks an existing integration that hasn't opted into it.
/// </summary>
internal static class IspModuleRestApiExtensions
{
    private const string OrderSpawnEventType = "order.spawn";

    public static IEndpointRouteBuilder MapIspModuleApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp")
    {
        routes.MapPost($"{prefix}/webhooks/ingest", IngestWebhookAsync)
        .WithName("IngestIspWebhook")
        .WithSummary("Webhook ingestion endpoint for the ISP vertical — validates envelope shape, dispatches order-spawn events to the matching BSS/OSS adapter, and shell-acknowledges everything else.")
        .Produces<IspWebhookAckResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        routes.MapGet($"{prefix}/geo-core/features/{{id}}", async (string id, IGeoCoreClient client, CancellationToken ct) =>
        {
            var feature = await client.GetFeatureByIdAsync(id, ct).ConfigureAwait(false);
            return feature is null ? Results.NotFound() : Results.Ok(feature);
        })
        .WithName("GetIspFeatureByIdViaGeoCore")
        .WithSummary("Resolves a feature by ID through IGeoCoreClient against the Core GeoAssets Server — proves this host's spatial/topology query wiring (XD01-279 AC1) for the ISP modules built on top of it.")
        .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> IngestWebhookAsync(
        [FromBody] IspWebhookIngestRequest? request, [FromServices] BssOssOrderAdapterRegistry adapterRegistry,
        [FromServices] BssOrderSpawnService spawnService, [FromServices] ITenantContext tenantContext, CancellationToken ct)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ProviderId) ||
            string.IsNullOrWhiteSpace(request.EventType) ||
            string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            return Results.BadRequest("ProviderId, EventType, and PayloadJson are all required.");
        }

        if (!IsWellFormedJson(request.PayloadJson))
            return Results.BadRequest("PayloadJson must be well-formed JSON.");

        if (!string.Equals(request.EventType, OrderSpawnEventType, StringComparison.Ordinal))
            return Results.Accepted(value: new IspWebhookAckResponse("Accepted"));

        return await DispatchOrderSpawnAsync(request, adapterRegistry, spawnService, tenantContext, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> DispatchOrderSpawnAsync(
        IspWebhookIngestRequest request, BssOssOrderAdapterRegistry adapterRegistry,
        BssOrderSpawnService spawnService, ITenantContext tenantContext, CancellationToken ct)
    {
        if (!adapterRegistry.TryResolve(request.ProviderId, out var adapter))
        {
            return Results.BadRequest(
                $"Unknown BSS/OSS provider '{request.ProviderId}'. Registered: {string.Join(", ", adapterRegistry.RegisteredProviderIds)}.");
        }

        try
        {
            var spec = adapter.ParseOrderSpawn(request.PayloadJson);
            var order = await spawnService.SpawnFtthDropInstallOrderAsync(
                adapter.ProviderId, spec, tenantContext.CurrentOrganizationId, ct).ConfigureAwait(false);
            return Results.Created(new Uri($"/api/field-service/orders/{order.Id}", UriKind.Relative), new IspWebhookAckResponse("Accepted"));
        }
        catch (BssOrderSpawnParseException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static bool IsWellFormedJson(string json)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(json);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
