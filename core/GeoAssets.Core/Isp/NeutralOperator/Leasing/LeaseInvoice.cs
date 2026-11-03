namespace GeoAssets.Core.Isp.NeutralOperator.Leasing;

/// <summary>One tenant ISP's monthly Poste Justo lease invoice (XD01-267).</summary>
public sealed record LeaseInvoice(string TenantCnpj, DateOnly BillingPeriod, IReadOnlyList<LeaseInvoiceLineItem> LineItems)
{
    public decimal Total => LineItems.Sum(item => item.Amount);
}
