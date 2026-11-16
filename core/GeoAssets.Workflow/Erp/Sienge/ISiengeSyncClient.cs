namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// The two Sienge REST operations the nightly sync needs (XD01-254): push cost-center postings
/// (machine-hour + hauling-ticket lines) and pull purchase orders for reconciliation. Each
/// method is independently retryable — <see cref="SiengeNightlySyncOrchestrator"/> wraps every
/// call with backoff and surfaces per-operation failure rather than letting one failing call
/// silently drop the other.
/// </summary>
public interface ISiengeSyncClient
{
    Task PushCostCenterPostingsAsync(IReadOnlyList<SiengeCostCenterPostingLine> postings, CancellationToken ct = default);

    Task<IReadOnlyList<SiengePurchaseOrderRecord>> PullPurchaseOrdersSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}
