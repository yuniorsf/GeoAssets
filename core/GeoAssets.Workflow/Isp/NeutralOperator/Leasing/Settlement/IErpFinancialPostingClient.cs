namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>Posts a settled invoice's financial record to the tenant's ERP. See <see cref="LeaseInvoiceErpPostingRequest"/>.</summary>
public interface IErpFinancialPostingClient
{
    Task PostAsync(LeaseInvoiceErpPostingRequest request, CancellationToken ct);
}
