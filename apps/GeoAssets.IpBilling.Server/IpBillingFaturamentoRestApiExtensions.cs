using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Maps the Faturamento ledger's legacy-parity manual entry / batch load / recarga / query
/// endpoints under <c>/api/ip-billing/faturamentos</c> (XD01-189) — kept for parity with the
/// real external-system-fed use case (the Distribuidora's own commercial billing system stays
/// the ultimate source of <c>KwhFaturado</c> even after XD01-188's auto-posting). See
/// <see cref="IpBillingOficioRestApiExtensions"/>'s doc comment for why this lives in
/// <c>GeoAssets.IpBilling.Server</c> rather than the ticket's own stale
/// <c>apps/GeoAssets.Server/...</c> path text.
///
/// The ticket's prose calls the audit log "FaturamentoLancamento" — the real, already-shipped
/// type (XD01-188) is <see cref="GeoAssets.IpBilling.Models.FaturamentoPosting"/>; every write
/// below goes through <see cref="IFaturamentoManualEntryService"/>, which appends one via
/// <see cref="IFaturamentoLedgerService.OverwriteManualEntryAsync"/> for every applied row.
/// </summary>
internal static class IpBillingFaturamentoRestApiExtensions
{
    internal sealed record ManualEntryRequest(Guid MunicipioId, string Competencia, decimal KwhFaturado, string? PerformedBy);

    internal sealed record BatchRequest(IReadOnlyList<FaturamentoBatchItem> Items, string? PerformedBy);

    internal sealed record FaturamentoAlreadyPostedResponse(string Message, Guid MunicipioId, string Competencia);

    public static void MapFaturamentoApi(RouteGroupBuilder group)
    {
        MapManualEntry(group);
        MapBatchLoad(group);
        MapRecarga(group);
        MapQuery(group);
    }

    private static void MapManualEntry(RouteGroupBuilder group)
    {
        group.MapPost("/faturamentos/manual-entry", async (
            [FromBody] ManualEntryRequest req,
            [FromServices] IFaturamentoManualEntryService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service
                    .ManualEntryAsync(req.MunicipioId, req.Competencia, req.KwhFaturado, req.PerformedBy, ct)
                    .ConfigureAwait(false);
                return Results.Created($"/api/ip-billing/faturamentos/{req.MunicipioId}?competencia={req.Competencia}", result);
            }
            catch (FaturamentoAlreadyPostedException ex)
            {
                return Results.Conflict(new FaturamentoAlreadyPostedResponse(ex.Message, ex.MunicipioId, ex.Competencia));
            }
        });
    }

    private static void MapBatchLoad(RouteGroupBuilder group)
    {
        group.MapPost("/faturamentos/batch-load", async (
            [FromBody] BatchRequest req,
            [FromServices] IFaturamentoManualEntryService service,
            CancellationToken ct) =>
        {
            var result = await service.BatchLoadAsync(req.Items, req.PerformedBy, ct).ConfigureAwait(false);
            return Results.Ok(result);
        });
    }

    private static void MapRecarga(RouteGroupBuilder group)
    {
        group.MapPost("/faturamentos/recarga", async (
            [FromBody] BatchRequest req,
            [FromServices] IFaturamentoManualEntryService service,
            CancellationToken ct) =>
        {
            var result = await service.RecargaAsync(req.Items, req.PerformedBy, ct).ConfigureAwait(false);
            return Results.Ok(result);
        });
    }

    private static void MapQuery(RouteGroupBuilder group)
    {
        // Extends the existing GET /faturamentos/{municipioId} (XD01-176) with an optional
        // competencia filter rather than adding a second, overlapping route — "query/consulta
        // by município and/or competência" per this ticket's AC.
        group.MapGet("/faturamentos/{municipioId:guid}/consulta", async (
            Guid municipioId,
            string? competencia,
            [FromServices] IFaturamentoRepository repo,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(competencia))
            {
                var item = await repo.GetByMunicipioAndCompetenciaAsync(municipioId, competencia, ct).ConfigureAwait(false);
                return item is not null ? Results.Ok(new[] { item }) : Results.Ok(Array.Empty<object>());
            }

            var items = await repo.GetByMunicipioAsync(municipioId, ct).ConfigureAwait(false);
            return Results.Ok(items);
        });
    }
}
