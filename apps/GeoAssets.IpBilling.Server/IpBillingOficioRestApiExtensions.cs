using GeoAssets.IpBilling.Oficio;
using GeoAssets.Provider.Xlsx;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Maps the Ofício upload/calculate/validate/supersede REST endpoints under
/// <c>/api/ip-billing/oficio</c> (XD01-186) — exposes XD01-185's
/// <see cref="IOficioCalculationService"/> over REST. Mirrors
/// <see cref="IpBillingFiscalizacaoRestApiExtensions"/>'s shape, per this ticket's own
/// description, plus the supersede endpoint for the duplicate-número correction path — see that
/// type's doc comment for why this lives in <c>GeoAssets.IpBilling.Server</c> rather than the
/// ticket's own stale <c>apps/GeoAssets.Server/...</c> path text.
///
/// Upload is a raw <c>multipart/form-data</c> file plus an optional
/// <c>numeroPrefeitura</c> query parameter — <see cref="OficioWorkbookRow"/> has no
/// NumeroPrefeitura column (it's document-level metadata, not a per-row value), so it travels
/// as a separate request parameter rather than a spreadsheet cell. Supersede is a thin wrapper
/// over <see cref="IOficioCalculationService.SupersedeAsync"/>: <c>POST .../{orderId}/supersede</c>
/// releases THAT order's claim on its current número, freeing it for a corrective re-upload on
/// a different order.
/// </summary>
internal static class IpBillingOficioRestApiExtensions
{
    internal sealed record OficioCalculateRequest(string? PerformedBy);

    internal sealed record OficioValidateRequest(string? PerformedBy);

    internal sealed record OficioSupersedeRequest(string? PerformedBy);

    internal sealed record DuplicateNumeroResponse(
        string Message, Guid MunicipioId, string NumeroPrefeitura, string ExistingServiceOrderId);

    public static void MapOficioApi(RouteGroupBuilder group)
    {
        MapUpload(group);
        MapCalculate(group);
        MapValidate(group);
        MapSupersede(group);
    }

    private static void MapUpload(RouteGroupBuilder group)
    {
        group.MapPost("/oficio/{orderId}/upload", async (
            string orderId,
            string? numeroPrefeitura,
            HttpRequest req,
            [FromServices] IWorkbookReader workbookReader,
            [FromServices] IOficioCalculationService calcService,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { message = "Expected multipart/form-data with a file." });

            var form = await req.ReadFormAsync(ct).ConfigureAwait(false);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null) return Results.BadRequest(new { message = "No file was uploaded." });

            using var stream = file.OpenReadStream();
            var rawRows = workbookReader.ReadRows(stream);
            var parseResult = OficioWorkbookRowParser.Parse(rawRows);
            if (parseResult.Errors.Count > 0) return Results.BadRequest(new { errors = parseResult.Errors });

            try
            {
                var count = await calcService.UploadAsync(orderId, numeroPrefeitura, parseResult.Rows, ct).ConfigureAwait(false);
                return Results.Ok(new { lineCount = count });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (DuplicateOficioNumeroException ex)
            {
                return Results.Conflict(new DuplicateNumeroResponse(ex.Message, ex.MunicipioId, ex.NumeroPrefeitura, ex.ExistingServiceOrderId));
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
        group.MapPost("/oficio/{orderId}/calculate", async (
            string orderId,
            [FromBody] OficioCalculateRequest? req,
            [FromServices] IOficioCalculationService calcService,
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
        group.MapPost("/oficio/{orderId}/validate", async (
            string orderId,
            [FromBody] OficioValidateRequest? req,
            [FromServices] IOficioCalculationService calcService,
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

    private static void MapSupersede(RouteGroupBuilder group)
    {
        group.MapPost("/oficio/{orderId}/supersede", async (
            string orderId,
            [FromBody] OficioSupersedeRequest? req,
            [FromServices] IOficioCalculationService calcService,
            CancellationToken ct) =>
        {
            try
            {
                await calcService.SupersedeAsync(orderId, req?.PerformedBy, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });
    }
}
