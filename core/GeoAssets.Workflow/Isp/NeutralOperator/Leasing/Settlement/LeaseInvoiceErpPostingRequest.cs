namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// GeoAssets-authored financial-posting payload sent to the tenant's configured ERP webhook
/// (SAP S/4HANA or TOTVS per the ticket's integration touchpoints — the wire shape is ours,
/// not either vendor's native schema, the same stance XD01-255 took for SAP WeBUY's confidential
/// XSD).
/// </summary>
public sealed record LeaseInvoiceErpPostingRequest(
    string TenantCnpj,
    DateOnly BillingPeriod,
    decimal TotalAmount,
    string NfComDocumentNumber,
    PaymentInstrumentKind PaymentInstrumentKind,
    string PaymentInstrumentExternalReference,
    string SettlementIdempotencyKey);
