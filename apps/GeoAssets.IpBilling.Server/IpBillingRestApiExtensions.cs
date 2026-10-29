using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Maps the <c>GEOS_FAT_IP</c> vertical microservice REST endpoints under <c>/api/ip-billing</c>.
/// Reference data (<c>Municipio</c>, <c>UnidadeOrganizacional</c>, <c>LampType</c>, <c>Faturamento</c>)
/// is served from <c>IpBillingDbContext</c>, while spatial/topological pole and 24h-lamp resolution
/// delegates to the Core Server via <see cref="IPoleResolver"/> (<see cref="HttpPoleResolver"/>)
/// and <see cref="IIpBillingCalculationService"/>.
/// </summary>
internal static class IpBillingRestApiExtensions
{
    internal sealed record CreateOrderRequest(
        string OrderTypeId,
        string Title,
        string MunicipioIbgeCode,
        string Competencia,
        string? CodigoInstalacao = null,
        string? CreatedBy = null);

    internal sealed record ValidateOrderRequest(string? PerformedBy = null);
    public static IEndpointRouteBuilder MapIpBillingApi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api/ip-billing")
    {
        var group = endpoints.MapGroup(prefix);

        MapReferenceDataEndpoints(group);
        MapResolutionEndpoints(group);
        MapOrderWorkflowEndpoints(group);

        return endpoints;
    }

    private static void MapReferenceDataEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/municipios", async ([FromServices] IMunicipioRepository repo, CancellationToken ct) =>
        {
            var items = await repo.GetAllAsync(ct).ConfigureAwait(false);
            return Results.Ok(items);
        });

        group.MapGet("/municipios/{ibgeCode}", async (
            string ibgeCode,
            [FromServices] IMunicipioRepository repo,
            CancellationToken ct) =>
        {
            var item = await repo.GetByIbgeCodeAsync(ibgeCode, ct).ConfigureAwait(false);
            return item is not null ? Results.Ok(item) : Results.NotFound();
        });

        group.MapGet("/municipios/{municipioId:guid}/unidades-organizacionais", async (
            Guid municipioId,
            [FromServices] IUnidadeOrganizacionalRepository repo,
            CancellationToken ct) =>
        {
            var items = await repo.GetByMunicipioAsync(municipioId, ct).ConfigureAwait(false);
            return Results.Ok(items);
        });

        group.MapGet("/lamp-types", async ([FromServices] ILampTypeRepository repo, CancellationToken ct) =>
        {
            var items = await repo.GetAllAsync(ct).ConfigureAwait(false);
            return Results.Ok(items);
        });

        group.MapGet("/faturamentos/{municipioId:guid}", async (
            Guid municipioId,
            [FromServices] IFaturamentoRepository repo,
            CancellationToken ct) =>
        {
            var items = await repo.GetByMunicipioAsync(municipioId, ct).ConfigureAwait(false);
            return Results.Ok(items);
        });
    }

    private static void MapResolutionEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/transformers/{ibgeCode}/{codigoInstalacao}/poles", async (
            string ibgeCode,
            string codigoInstalacao,
            [FromServices] ITransformerResolver transformerResolver,
            [FromServices] IPoleResolver poleResolver,
            CancellationToken ct) =>
        {
            var transformerId = await transformerResolver
                .ResolveTransformerFeatureIdAsync(ibgeCode, codigoInstalacao, ct)
                .ConfigureAwait(false);

            if (transformerId is null)
            {
                return Results.NotFound();
            }

            var poles = await poleResolver
                .ResolvePoleFeatureIdsAsync(transformerId.Value, ct)
                .ConfigureAwait(false);

            return Results.Ok(poles);
        });
    }

    private static void MapOrderWorkflowEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/orders", async (
            [FromBody] CreateOrderRequest req,
            [FromServices] IServiceOrderRepository orderRepo,
            [FromServices] TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var order = BuildNewOrder(req, timeProvider);
            await orderRepo.AddAsync(order, ct).ConfigureAwait(false);
            return Results.Created($"/api/ip-billing/orders/{order.Id}", order);
        });

        group.MapGet("/orders/{id}", async (
            string id,
            [FromServices] IServiceOrderRepository orderRepo,
            CancellationToken ct) =>
        {
            var order = await orderRepo.GetByIdAsync(id, ct).ConfigureAwait(false);
            return order is not null ? Results.Ok(order) : Results.NotFound();
        });

        group.MapPost("/orders/{id}/calculate", async (
            string id,
            [FromBody] IpBillingCalculationRequest? req,
            [FromServices] IIpBillingCalculationService calcService,
            CancellationToken ct) =>
        {
            var result = await calcService.CalculateOrderAsync(id, req, ct).ConfigureAwait(false);
            return Results.Ok(result);
        });

        group.MapPost("/orders/{id}/validate", async (
            string id,
            [FromBody] ValidateOrderRequest? req,
            [FromServices] IIpBillingCalculationService calcService,
            CancellationToken ct) =>
        {
            var result = await calcService.ValidateOrderAsync(id, req?.PerformedBy, ct).ConfigureAwait(false);
            return Results.Ok(result);
        });
    }

    private static ServiceOrder BuildNewOrder(CreateOrderRequest req, TimeProvider timeProvider)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IpBillingCalculationService.AttrMunicipioIbgeCode] = req.MunicipioIbgeCode,
            [IpBillingCalculationService.AttrCompetencia]       = req.Competencia,
            ["scopeId"]                                         = req.MunicipioIbgeCode,
        };
        if (!string.IsNullOrWhiteSpace(req.CodigoInstalacao))
        {
            attributes[IpBillingCalculationService.AttrCodigoInstalacao] = req.CodigoInstalacao;
        }

        return new ServiceOrder
        {
            Id          = Guid.NewGuid().ToString(),
            OrderTypeId = req.OrderTypeId,
            Title       = req.Title,
            Status      = IpBillingWorkflowGraph.Carregado,
            CreatedBy   = req.CreatedBy ?? "system",
            CreatedAt   = timeProvider.GetUtcNow().UtcDateTime,
            Attributes  = attributes,
        };
    }
}
