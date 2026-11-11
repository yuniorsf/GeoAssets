namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// Runs XD01-254's two nightly Sienge sync operations — machine-hour/hauling-ticket cost-center
/// posting push, and purchase-order reconciliation — each independently retried with backoff via
/// <see cref="SiengeRetryExecutor"/>. One operation's exhausted-retry failure doesn't block the
/// other; it's captured in the returned <see cref="SiengeNightlySyncRunResult"/> instead.
/// "Nightly" itself is a scheduling concern for the host (a cron trigger hitting the
/// sync-trigger endpoint) — this orchestrator only knows how to run one pass.
/// </summary>
public sealed class SiengeNightlySyncOrchestrator(
    ISiengeSyncClient syncClient,
    IMachineHourSource machineHourSource,
    IHaulingTicketSource haulingTicketSource,
    IFieldConsumptionSource consumptionSource,
    SiengeSyncRetryOptions retryOptions,
    TimeProvider timeProvider)
{
    public const decimal DefaultDiscrepancyToleranceFraction = 0.02m;

    public async Task<SiengeNightlySyncRunResult> RunAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var (postedCount, postingFailure) = await RunCostCenterPostingAsync(sinceUtc, ct).ConfigureAwait(false);
        var (flags, reconciliationFailure) = await RunPurchaseOrderReconciliationAsync(sinceUtc, ct).ConfigureAwait(false);

        return new SiengeNightlySyncRunResult(postedCount, flags, postingFailure, reconciliationFailure);
    }

    private async Task<(int Count, Exception? Failure)> RunCostCenterPostingAsync(DateTime sinceUtc, CancellationToken ct)
    {
        try
        {
            var machineHours = await machineHourSource.GetMachineHoursSinceAsync(sinceUtc, ct).ConfigureAwait(false);
            var haulingTickets = await haulingTicketSource.GetHaulingTicketsSinceAsync(sinceUtc, ct).ConfigureAwait(false);
            var lines = SiengeCostCenterPostingMapper.Map(machineHours, haulingTickets);

            await SiengeRetryExecutor.ExecuteWithBackoffAsync(
                async innerCt => { await syncClient.PushCostCenterPostingsAsync(lines, innerCt).ConfigureAwait(false); return true; },
                retryOptions, timeProvider, ct).ConfigureAwait(false);

            return (lines.Count, null);
        }
        catch (Exception ex)
        {
            return (0, ex);
        }
    }

    private async Task<(IReadOnlyList<ConsumptionDiscrepancyFlag> Flags, Exception? Failure)> RunPurchaseOrderReconciliationAsync(DateTime sinceUtc, CancellationToken ct)
    {
        try
        {
            var purchaseOrders = await SiengeRetryExecutor.ExecuteWithBackoffAsync(
                innerCt => syncClient.PullPurchaseOrdersSinceAsync(sinceUtc, innerCt), retryOptions, timeProvider, ct).ConfigureAwait(false);
            var consumption = await consumptionSource.GetConsumptionSinceAsync(sinceUtc, ct).ConfigureAwait(false);

            var flags = PurchaseOrderReconciler.Reconcile(purchaseOrders, consumption, DefaultDiscrepancyToleranceFraction);
            return (flags, null);
        }
        catch (Exception ex)
        {
            return ([], ex);
        }
    }
}
