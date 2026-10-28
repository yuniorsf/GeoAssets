using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Maps the <c>GEOS_FAT_IP</c> vertical microservice REST endpoints under <c>/api/ip-billing</c>.
/// Reference data (<c>Municipio</c>, <c>UnidadeOrganizacional</c>, <c>LampType</c>, <c>Faturamento</c>)
/// is served from <c>IpBillingDbContext</c>, while spatial/topological pole resolution delegates
/// to the Core Server via <see cref="IPoleResolver"/> (<see cref="HttpPoleResolver"/>).
/// </summary>
internal static class IpBillingRestApiExtensions
{
    public static IEndpointRouteBuilder MapIpBillingApi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api/ip-billing")
    {
        var group = endpoints.MapGroup(prefix);

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

        MapResolutionEndpoints(group);

        return endpoints;
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
}
