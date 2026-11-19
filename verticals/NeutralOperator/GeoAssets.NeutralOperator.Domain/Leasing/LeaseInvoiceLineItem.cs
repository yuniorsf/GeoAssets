namespace GeoAssets.NeutralOperator.Domain.Leasing;

public sealed record LeaseInvoiceLineItem(string Description, decimal Amount, LeaseInvoiceLineItemKind Kind);
