using GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the lease-invoice settlement endpoints (XD01-268): triggering a settlement run over a
/// batch of invoices (NF Com → payment instrument → ERP posting), and receiving the bank/PSP's
/// paid-settlement webhook for reconciliation.
/// </summary>
internal static class LeaseInvoiceSettlementApiExtensions
{
    public static IEndpointRouteBuilder MapLeaseInvoiceSettlementApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp/lease-settlement")
    {
        routes.MapPost(prefix, async (
            LeaseInvoiceSettlementRunRequest request,
            LeaseInvoiceSettlementOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            var result = await orchestrator.SettleAsync(request.Invoices, request.PaymentInstrumentKind, ct).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("RunLeaseInvoiceSettlement")
        .WithSummary("Runs NF Com issuance, payment-instrument issuance, and ERP posting for a batch of lease invoices. Idempotent — already-posted invoices are skipped, not re-issued.")
        .Produces<LeaseInvoiceSettlementRunResult>();

        routes.MapPost($"{prefix}/webhook", ReconcileAsync)
        .WithName("ReconcileLeaseInvoiceSettlement")
        .WithSummary("Receives a bank/PSP settlement-paid webhook and marks the matching invoice's settlement record Paid.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        return routes;
    }

    private static async Task<IResult> ReconcileAsync(
        LeaseInvoiceSettlementWebhookRequest request,
        LeaseInvoiceSettlementOrchestrator orchestrator,
        CancellationToken ct)
    {
        try
        {
            await orchestrator.ReconcileSettlementAsync(
                new SettlementWebhookPayload(request.TenantCnpj, request.BillingPeriod, request.PaidAtUtc, request.ExternalReference),
                ct).ConfigureAwait(false);
            return Results.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
}
