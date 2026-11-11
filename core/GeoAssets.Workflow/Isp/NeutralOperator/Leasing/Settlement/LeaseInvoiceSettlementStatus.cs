namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// Resumable progress of one <see cref="GeoAssets.Core.Isp.NeutralOperator.Leasing.LeaseInvoice"/>'s
/// settlement (XD01-268). Ordered so a retry after a failed step only redoes work from that step
/// onward — never re-issues a document/payment instrument that already succeeded.
/// </summary>
public enum LeaseInvoiceSettlementStatus
{
    Pending = 0,
    NfComIssued = 1,
    PaymentInstrumentIssued = 2,
    PostedToErp = 3,
    Paid = 4,
}
