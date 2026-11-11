using GeoAssets.Core.Isp.NeutralOperator.Leasing;

namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// Issues one <see cref="Kind"/> of settlement instrument for a <see cref="LeaseInvoice"/>.
/// <see cref="LeaseInvoiceSettlementOrchestrator"/> resolves the right issuer from a registered
/// collection by <see cref="Kind"/>; an unregistered kind fails orchestration with a clear
/// <see cref="InvalidOperationException"/> rather than silently no-opping.
/// </summary>
public interface IPaymentInstrumentIssuer
{
    PaymentInstrumentKind Kind { get; }

    Task<PaymentInstrumentIssuanceResult> IssueAsync(
        LeaseInvoice invoice,
        string settlementIdempotencyKey,
        CancellationToken ct);
}
