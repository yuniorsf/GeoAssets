using System.Globalization;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.Provider.Xlsx;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Mvc;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Maps the Levantamento upload/calculate/validate/memória-de-cálculo REST endpoints under
/// <c>/api/ip-billing/levantamento</c> (XD01-180) — exposes XD01-179's
/// <see cref="ILevantamentoCalculationService"/> over REST.
///
/// <strong>Deviation from this ticket's own stated file path:</strong> XD01-180's own text names
/// <c>apps/GeoAssets.Server/IpBillingLevantamentoRestApiExtensions.cs</c> as the target, but
/// <c>GeoAssets.Server</c> is architecturally forbidden from referencing any
/// <c>GeoAssets.IpBilling*</c> assembly (see <c>ServerArchitectureTests.
/// ServerAssembly_ShouldNotReference_IpBillingVerticalAssemblies</c>) — the same stale-ticket-
/// text-vs-enforced-architecture gap XD01-177 hit and resolved the same way. This lives in
/// <c>GeoAssets.IpBilling.Server</c> instead, alongside every other IpBilling REST endpoint
/// (<see cref="IpBillingRestApiExtensions"/>).
///
/// Upload is a raw <c>multipart/form-data</c> file (server-side-only ClosedXML parsing via
/// <see cref="IWorkbookReader"/>); the Blazor client that eventually posts here (XD01-181) does
/// a plain byte upload, not a client-side GeoJSON-style text parse. Calculate/Validate take an
/// optional JSON body. "Transformer code not found" is deliberately <em>not</em> a blocking
/// upload-time error — it's an already-shipped, non-blocking Calculate-time outcome
/// (<see cref="LevantamentoCalculationResult.UnresolvedCodigosInstalacao"/>, XD01-179): billing
/// still proceeds for an unresolved transformer, so rejecting the file for it here would
/// contradict that deliberate design.
/// </summary>
internal static class IpBillingLevantamentoRestApiExtensions
{
    internal sealed record LevantamentoCalculateRequest(DateOnly? AsOf, string? PerformedBy);

    internal sealed record LevantamentoValidateRequest(string? PerformedBy);

    internal sealed record LevantamentoMemoriaCalculoResponse(
        LevantamentoHeader Header,
        string OrderStatus,
        IReadOnlyList<LevantamentoTransformerLine> TransformerLines,
        IReadOnlyList<Levantamento24hLine> Lines24h,
        decimal NormalKwh,
        decimal RetroativoKwh,
        decimal Irregular24hKwh,
        decimal TotalCalculatedKwh);

    public static void MapLevantamentoApi(RouteGroupBuilder group)
    {
        MapUpload(group);
        MapCalculate(group);
        MapValidate(group);
        MapMemoriaCalculo(group);
    }

    private static void MapUpload(RouteGroupBuilder group)
    {
        group.MapPost("/levantamento/{orderId}/upload", async (
            string orderId,
            HttpRequest req,
            [FromServices] IWorkbookReader workbookReader,
            [FromServices] ILevantamentoCalculationService calcService,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { message = "Expected multipart/form-data with a file." });

            var form = await req.ReadFormAsync(ct).ConfigureAwait(false);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null) return Results.BadRequest(new { message = "No file was uploaded." });

            using var stream = file.OpenReadStream();
            var rawRows = workbookReader.ReadRows(stream);
            var parseResult = LevantamentoWorkbookRowParser.Parse(rawRows);
            if (parseResult.Errors.Count > 0) return Results.BadRequest(new { errors = parseResult.Errors });

            try
            {
                var count = await calcService.UploadAsync(orderId, parseResult.Rows, ct).ConfigureAwait(false);
                return Results.Ok(new { transformerLineCount = count });
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
        group.MapPost("/levantamento/{orderId}/calculate", async (
            string orderId,
            [FromBody] LevantamentoCalculateRequest? req,
            [FromServices] ILevantamentoCalculationService calcService,
            CancellationToken ct) =>
        {
            try
            {
                var result = await calcService.CalculateStoredAsync(orderId, req?.AsOf, req?.PerformedBy, ct).ConfigureAwait(false);
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
        group.MapPost("/levantamento/{orderId}/validate", async (
            string orderId,
            [FromBody] LevantamentoValidateRequest? req,
            [FromServices] ILevantamentoCalculationService calcService,
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

    private static void MapMemoriaCalculo(RouteGroupBuilder group)
    {
        group.MapGet("/levantamento/{orderId}/memoria-calculo", async (
            string orderId,
            [FromServices] IServiceOrderRepository orderRepo,
            [FromServices] ILevantamentoRepository levantamentoRepo,
            CancellationToken ct) =>
        {
            var order = await orderRepo.GetByIdAsync(orderId, ct).ConfigureAwait(false);
            if (order is null) return Results.NotFound();

            var header = await levantamentoRepo.GetHeaderAsync(orderId, ct).ConfigureAwait(false);
            if (header is null) return Results.NotFound();

            var transformerLines = await levantamentoRepo.GetTransformerLinesAsync(orderId, ct).ConfigureAwait(false);
            var lines24h = await levantamentoRepo.Get24hLinesAsync(orderId, ct).ConfigureAwait(false);

            return Results.Ok(new LevantamentoMemoriaCalculoResponse(
                header,
                order.Status,
                transformerLines,
                lines24h,
                ReadDecimalAttr(order, IpBillingCalculationService.AttrNormalKwh),
                ReadDecimalAttr(order, LevantamentoCalculationService.AttrRetroativoKwh),
                ReadDecimalAttr(order, IpBillingCalculationService.AttrIrregular24hKwh),
                ReadDecimalAttr(order, IpBillingCalculationService.AttrCalculatedKwh)));
        });
    }

    private static decimal ReadDecimalAttr(IServiceOrder order, string key) =>
        order.Attributes.TryGetValue(key, out var raw) &&
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
}
