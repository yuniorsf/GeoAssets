using GeoAssets.Core.Interfaces;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// ISP vertical host scaffolding (XD01-279): a webhook ingestion endpoint shell and a thin
/// <see cref="IGeoCoreClient"/> proxy, mirroring the Electrical/Sanitation/NeutralPole archetype
/// modules' own <c>MapXModuleApi</c> shape (<see cref="ElectricalModuleRestApiExtensions"/> et
/// al.) so the BSS/OSS webhook integration (XD01-280), CTO splitter capacity tracking (XD01-281),
/// OTDR fault dispatch (XD01-283), and compliance dossier exporter (XD01-284) tickets register
/// their real logic behind these two routes rather than re-deriving host wiring of their own.
/// </summary>
internal static class IspModuleRestApiExtensions
{
    public static IEndpointRouteBuilder MapIspModuleApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp")
    {
        routes.MapPost($"{prefix}/webhooks/ingest", (IspWebhookIngestRequest? request) =>
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

            // Shell acceptance only — XD01-280 replaces this with real per-ProviderId dispatch
            // (IXC Soft, MK-Auth, SGP, Voalle) once that integration lands.
            return Results.Accepted(value: new IspWebhookAckResponse("Accepted"));
        })
        .WithName("IngestIspWebhook")
        .WithSummary("Shell webhook ingestion endpoint for the ISP vertical — validates envelope shape and acknowledges; XD01-280 adds real per-provider dispatch.")
        .Produces<IspWebhookAckResponse>(StatusCodes.Status202Accepted)
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
