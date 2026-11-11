namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>Outcome of one <see cref="LeaseInvoiceSettlementOrchestrator.SettleAsync"/> batch run.</summary>
public sealed record LeaseInvoiceSettlementRunResult(
    int InvoiceCount,
    int SettledCount,
    int AlreadySettledSkippedCount,
    IReadOnlyList<(string TenantCnpj, string Reason)> Failures);
