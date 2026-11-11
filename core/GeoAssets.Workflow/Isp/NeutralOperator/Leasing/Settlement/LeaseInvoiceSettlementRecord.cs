namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// Persisted settlement progress for one tenant's one billing period — the idempotency record
/// that lets a retried run resume instead of double-issuing (XD01-268 AC: "Integration failure
/// (ERP unreachable) retries without double-invoicing"). Keyed by
/// (<paramref name="TenantCnpj"/>, <paramref name="BillingPeriod"/>).
/// </summary>
public sealed record LeaseInvoiceSettlementRecord(
    string TenantCnpj,
    DateOnly BillingPeriod,
    LeaseInvoiceSettlementStatus Status,
    string? NfComDocumentNumber = null,
    PaymentInstrumentIssuanceResult? PaymentInstrument = null,
    string? FailureReason = null,
    DateTimeOffset? PaidAtUtc = null)
{
    public string IdempotencyKey => $"{TenantCnpj}:{BillingPeriod:yyyy-MM}";
}
