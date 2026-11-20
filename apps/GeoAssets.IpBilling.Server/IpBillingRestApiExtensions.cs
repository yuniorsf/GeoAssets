using System.Security.Claims;
using GeoAssets.Identity.Authentication;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
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
///
/// Every endpoint requires an authenticated caller via <c>AddGeoAssetsAuthentication</c>'s
/// global fallback policy (XD01-177) — the same authenticated-caller-only bar as
/// <c>GeoAssets.FieldService.Server</c>, not the finer <c>ip-billing:read</c>/
/// <c>ip-billing:reference-manage</c> permission codes this vertical publishes to the kernel
/// (<see cref="IpBillingPermissionContributor"/>). Since XD01-385 this host can evaluate them — the
/// caller's permissions are the kernel's answer for its token (<c>AddKernelIdentity</c>) — and the
/// município grant endpoints (<see cref="MunicipioGrantsRestApiExtensions"/>) do; these endpoints
/// keep their authenticated-caller bar.
///
/// <strong>Município writes sync the Prefeitura's scoped <c>OrganizationGrant</c> themselves</strong>
/// (XD01-395): a create, update or delete that changes the grant (<see cref="MunicipioGrantSyncPlanner"/>)
/// queues the change in the same transaction as the save (<see cref="IMunicipioGrantSyncQueue"/>),
/// requested by the caller's user id from its token, and <see cref="MunicipioGrantSyncDispatcher"/>
/// sends it to the kernel afterwards — a kernel outage never fails the save. The município reads
/// report where it stands (<c>grantSyncState</c>, <c>grantSyncError</c>, <c>grantSyncedAt</c>);
/// <c>/api/ip-billing/municipio-grants/sync|revoke</c> stay for manual repair.
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

    internal sealed record MunicipioWriteRequest(
        string IbgeCode,
        string Name,
        string Uf,
        Guid DistribuidoraOrganizationId,
        Guid? PrefeituraOrganizationId,
        OrigemWorkflow OrigemLevantamento = OrigemWorkflow.Planilha,
        OrigemWorkflow OrigemFiscalizacao = OrigemWorkflow.Planilha,
        OrigemWorkflow OrigemOficio = OrigemWorkflow.Planilha,
        OrigemWorkflow OrigemMemoriaCalculo = OrigemWorkflow.Planilha,
        Guid? DistribuidoraId = null);

    internal sealed record UnidadeOrganizacionalWriteRequest(Guid MunicipioId, string Name, string? Code);

    public static IEndpointRouteBuilder MapIpBillingApi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api/ip-billing")
    {
        var group = endpoints.MapGroup(prefix);

        MapReferenceDataEndpoints(group);
        MapReferenceDataWriteEndpoints(group);
        MapResolutionEndpoints(group);
        MapOrderWorkflowEndpoints(group);
        IpBillingLevantamentoRestApiExtensions.MapLevantamentoApi(group);
        IpBillingFiscalizacaoRestApiExtensions.MapFiscalizacaoApi(group);
        IpBillingOficioRestApiExtensions.MapOficioApi(group);
        IpBillingFaturamentoRestApiExtensions.MapFaturamentoApi(group);
        IpBillingDistribuidoraRestApiExtensions.MapDistribuidoraApi(group);

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

        group.MapGet("/unidades-organizacionais/{id:guid}", async (
            Guid id,
            [FromServices] IUnidadeOrganizacionalRepository repo,
            CancellationToken ct) =>
        {
            var item = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            return item is not null ? Results.Ok(item) : Results.NotFound();
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

    private static void MapReferenceDataWriteEndpoints(RouteGroupBuilder group)
    {
        MapMunicipioWriteEndpoints(group);
        MapUnidadeOrganizacionalWriteEndpoints(group);
    }

    private static void MapMunicipioWriteEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/municipios", async (
            [FromBody] MunicipioWriteRequest req,
            [FromServices] IMunicipioRepository repo,
            [FromServices] IMunicipioGrantSyncQueue grantSync,
            ClaimsPrincipal caller,
            CancellationToken ct) =>
        {
            var municipio = new Municipio
            {
                IbgeCode = req.IbgeCode,
                DistribuidoraOrganizationId = req.DistribuidoraOrganizationId,
            };
            ApplyMunicipioWrite(municipio, req);
            await repo.AddAsync(municipio, ct).ConfigureAwait(false);
            QueueGrantSync(grantSync, caller, municipio, before: null, after: MunicipioGrantLink.Of(municipio));
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/ip-billing/municipios/{municipio.IbgeCode}", municipio);
        });

        group.MapPut("/municipios/{id:guid}", async (
            Guid id,
            [FromBody] MunicipioWriteRequest req,
            [FromServices] IMunicipioRepository repo,
            [FromServices] IMunicipioGrantSyncQueue grantSync,
            ClaimsPrincipal caller,
            CancellationToken ct) =>
        {
            var municipio = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (municipio is null) return Results.NotFound();

            var before = MunicipioGrantLink.Of(municipio);
            ApplyMunicipioWrite(municipio, req);
            await repo.UpdateAsync(municipio, ct).ConfigureAwait(false);
            QueueGrantSync(grantSync, caller, municipio, before, MunicipioGrantLink.Of(municipio));
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        group.MapDelete("/municipios/{id:guid}", async (
            Guid id,
            [FromServices] IMunicipioRepository repo,
            [FromServices] IMunicipioGrantSyncQueue grantSync,
            ClaimsPrincipal caller,
            CancellationToken ct) =>
        {
            var municipio = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            await repo.DeleteAsync(id, ct).ConfigureAwait(false);
            if (municipio is not null)
                QueueGrantSync(grantSync, caller, municipio, MunicipioGrantLink.Of(municipio), after: null);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Queues the grant change the save of <paramref name="municipio"/> calls for, if any (XD01-395), on the save's own unit
    /// of work, and reports it on <paramref name="municipio"/> as pending.
    /// </summary>
    private static void QueueGrantSync(
        IMunicipioGrantSyncQueue grantSync,
        ClaimsPrincipal caller,
        Municipio municipio,
        MunicipioGrantLink? before,
        MunicipioGrantLink? after)
    {
        var change = MunicipioGrantSyncPlanner.Plan(before, after);
        if (change is null) return;

        grantSync.Enqueue(change, CallerId(caller));
        municipio.GrantSyncState = MunicipioGrantSyncState.Pending;
        municipio.GrantSyncError = null;
        municipio.GrantSyncedAt = null;
    }

    /// <summary>
    /// The caller's user id, from its token's claims only (never a header): the IdP object id, else the name identifier or
    /// subject, else the calling application's id.
    /// </summary>
    internal static string CallerId(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        string?[] candidates =
        [
            ClaimMapping.EntraDefault.Map(caller)?.ExternalObjectId,
            caller.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            caller.FindFirst("sub")?.Value,
            caller.FindFirst("azp")?.Value,
            caller.FindFirst("appid")?.Value,
        ];

        return candidates.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? UnknownCaller;
    }

    /// <summary>What <see cref="CallerId"/> answers for a principal without any identifying claim.</summary>
    internal const string UnknownCaller = "unknown";

    private static void ApplyMunicipioWrite(Municipio municipio, MunicipioWriteRequest req)
    {
        municipio.IbgeCode = req.IbgeCode;
        municipio.Name = req.Name;
        municipio.Uf = req.Uf;
        municipio.DistribuidoraOrganizationId = req.DistribuidoraOrganizationId;
        municipio.PrefeituraOrganizationId = req.PrefeituraOrganizationId;
        municipio.DistribuidoraId = req.DistribuidoraId;
        municipio.OrigemLevantamento = req.OrigemLevantamento;
        municipio.OrigemFiscalizacao = req.OrigemFiscalizacao;
        municipio.OrigemOficio = req.OrigemOficio;
        municipio.OrigemMemoriaCalculo = req.OrigemMemoriaCalculo;
    }

    private static void MapUnidadeOrganizacionalWriteEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/unidades-organizacionais", async (
            [FromBody] UnidadeOrganizacionalWriteRequest req,
            [FromServices] IUnidadeOrganizacionalRepository repo,
            CancellationToken ct) =>
        {
            var unidade = new UnidadeOrganizacional
            {
                MunicipioId = req.MunicipioId,
                Name = req.Name,
                Code = req.Code,
            };
            await repo.AddAsync(unidade, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/ip-billing/unidades-organizacionais/{unidade.Id}", unidade);
        });

        group.MapPut("/unidades-organizacionais/{id:guid}", async (
            Guid id,
            [FromBody] UnidadeOrganizacionalWriteRequest req,
            [FromServices] IUnidadeOrganizacionalRepository repo,
            CancellationToken ct) =>
        {
            var unidade = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (unidade is null) return Results.NotFound();

            unidade.MunicipioId = req.MunicipioId;
            unidade.Name = req.Name;
            unidade.Code = req.Code;
            await repo.UpdateAsync(unidade, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        group.MapDelete("/unidades-organizacionais/{id:guid}", async (
            Guid id,
            [FromServices] IUnidadeOrganizacionalRepository repo,
            CancellationToken ct) =>
        {
            await repo.DeleteAsync(id, ct).ConfigureAwait(false);
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
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
            [IpBillingCalculationService.AttrCompetencia] = req.Competencia,
            ["scopeId"] = req.MunicipioIbgeCode,
        };
        if (!string.IsNullOrWhiteSpace(req.CodigoInstalacao))
        {
            attributes[IpBillingCalculationService.AttrCodigoInstalacao] = req.CodigoInstalacao;
        }

        return new ServiceOrder
        {
            Id = Guid.NewGuid().ToString(),
            OrderTypeId = req.OrderTypeId,
            Title = req.Title,
            Status = IpBillingWorkflowGraph.Carregado,
            CreatedBy = req.CreatedBy ?? "system",
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            Attributes = attributes,
        };
    }
}
