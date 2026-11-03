namespace GeoAssets.Core.Isp.NeutralOperator.Leasing;

public sealed record LeaseInvoiceLineItem(string Description, decimal Amount, LeaseInvoiceLineItemKind Kind);
