namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// The three Totvs Protheus REST operations the nightly sync needs (XD01-253): pull technician
/// certification/equipment-calibration status, push the approved-postings payroll export, and
/// pull warehouse stock issuances for shrinkage reconciliation. Each method is independently
/// retryable — <c>TotvsNightlySyncOrchestrator</c> wraps every call with backoff and surfaces
/// per-operation failure rather than letting one failing call silently drop the others.
/// </summary>
public interface ITotvsProtheusSyncClient
{
    Task<IReadOnlyList<TechnicianComplianceRecord>> PullTechnicianComplianceAsync(CancellationToken ct = default);

    Task PushPayrollExportAsync(IReadOnlyList<ProtheusPayrollExportLine> lines, CancellationToken ct = default);

    Task<IReadOnlyList<WarehouseStockIssuanceRecord>> PullWarehouseStockIssuancesAsync(DateTime sinceUtc, CancellationToken ct = default);
}
