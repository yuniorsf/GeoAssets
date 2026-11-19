using GeoAssets.NeutralOperator.Domain.Leasing;
using GeoAssets.NeutralOperator.Application.Leasing.Settlement;

namespace GeoAssets.FieldService.Server;

/// <summary>Request body for triggering a settlement run over a caller-supplied batch of invoices.</summary>
internal sealed record LeaseInvoiceSettlementRunRequest(IReadOnlyList<LeaseInvoice> Invoices, PaymentInstrumentKind PaymentInstrumentKind);
