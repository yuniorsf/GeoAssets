namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>Persistence port for <see cref="LeaseInvoiceSettlementRecord"/> — the orchestrator's idempotency store.</summary>
public interface ILeaseInvoiceSettlementRepository
{
    Task<LeaseInvoiceSettlementRecord?> TryGetAsync(string tenantCnpj, DateOnly billingPeriod, CancellationToken ct);

    Task SaveAsync(LeaseInvoiceSettlementRecord record, CancellationToken ct);
}
