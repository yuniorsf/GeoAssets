using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.Provider.Xlsx;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Maps the Fiscalização upload/calculate/validate REST endpoints under
/// <c>/api/ip-billing/fiscalizacao</c> (XD01-183) — exposes XD01-182's
/// <see cref="IFiscalizacaoCalculationService"/> over REST. Mirrors
/// <see cref="IpBillingLevantamentoRestApiExtensions"/>'s shape exactly, per this ticket's own
/// description — see that type's doc comment for why this lives in
/// <c>GeoAssets.IpBilling.Server</c> rather than the ticket's own stale
/// <c>apps/GeoAssets.Server/...</c> path text (the same <c>GeoAssets.Server</c>-cannot-
/// reference-<c>GeoAssets.IpBilling*</c> architecture boundary applies here).
///
/// Upload is a raw <c>multipart/form-data</c> file (server-side-only ClosedXML parsing via
/// <see cref="IWorkbookReader"/>); Calculate/Validate take an optional JSON body. There is no
/// memória-de-cálculo endpoint here — unlike Levantamento, a Fiscalização's calculated totals
/// are returned directly by <c>/calculate</c> and there is no separate UI-facing read model for
/// it (out of this ticket's acceptance criteria).
/// </summary>
internal static class IpBillingFiscalizacaoRestApiExtensions
{
    internal sealed record FiscalizacaoCalculateRequest(string? PerformedBy);

    internal sealed record FiscalizacaoValidateRequest(string? PerformedBy);

    public static void MapFiscalizacaoApi(RouteGroupBuilder group)
    {
        MapUpload(group);
        MapCalculate(group);
        MapValidate(group);
    }

    private static void MapUpload(RouteGroupBuilder group)
    {
        group.MapPost("/fiscalizacao/{orderId}/upload", async (
            string orderId,
            HttpRequest req,
            [FromServices] IWorkbookReader workbookReader,
            [FromServices] IFiscalizacaoCalculationService calcService,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { message = "Expected multipart/form-data with a file." });

            var form = await req.ReadFormAsync(ct).ConfigureAwait(false);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null) return Results.BadRequest(new { message = "No file was uploaded." });

            using var stream = file.OpenReadStream();
            var rawRows = workbookReader.ReadRows(stream);
            var parseResult = FiscalizacaoWorkbookRowParser.Parse(rawRows);
            if (parseResult.Errors.Count > 0) return Results.BadRequest(new { errors = parseResult.Errors });

            try
            {
                var count = await calcService.UploadAsync(orderId, parseResult.Rows, ct).ConfigureAwait(false);
                return Results.Ok(new { lineCount = count });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
            .DisableAntiforgery();
    }

    private static void MapCalculate(RouteGroupBuilder group)
    {
        group.MapPost("/fiscalizacao/{orderId}/calculate", async (
            string orderId,
            [FromBody] FiscalizacaoCalculateRequest? req,
            [FromServices] IFiscalizacaoCalculationService calcService,
            CancellationToken ct) =>
        {
            try
            {
                var result = await calcService.CalculateStoredAsync(orderId, req?.PerformedBy, ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (InvalidServiceOrderTransitionException ex)
            {
                return Results.BadRequest(new { from = ex.From, to = ex.To });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });
    }

    private static void MapValidate(RouteGroupBuilder group)
    {
        group.MapPost("/fiscalizacao/{orderId}/validate", async (
            string orderId,
            [FromBody] FiscalizacaoValidateRequest? req,
            [FromServices] IFiscalizacaoCalculationService calcService,
            CancellationToken ct) =>
        {
            try
            {
                var result = await calcService.ValidateAsync(orderId, req?.PerformedBy, ct).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (InvalidServiceOrderTransitionException ex)
            {
                return Results.BadRequest(new { from = ex.From, to = ex.To });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });
    }
}
