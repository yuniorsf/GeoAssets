namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// Runs XD01-253's three nightly Totvs Protheus sync operations — technician compliance pull,
/// approved-postings payroll export push, and warehouse-stock shrinkage reconciliation — each
/// independently retried with backoff via <see cref="TotvsRetryExecutor"/>. One operation's
/// exhausted-retry failure doesn't block the other two; it's captured in the returned
/// <see cref="TotvsNightlySyncRunResult"/> instead (the AC's "surfaced, not silently dropped").
/// "Nightly" itself is a scheduling concern for the host (a cron trigger hitting the
/// sync-trigger endpoint) — this orchestrator only knows how to run one pass.
/// </summary>
public sealed class TotvsNightlySyncOrchestrator(
    ITotvsProtheusSyncClient syncClient,
    ITechnicianComplianceRepository complianceRepository,
    IApprovedUsPostingSource postingSource,
    IFieldMaterialConsumptionSource consumptionSource,
    TotvsSyncRetryOptions retryOptions,
    TimeProvider timeProvider)
{
    public const decimal DefaultShrinkageToleranceFraction = 0.02m;

    public async Task<TotvsNightlySyncRunResult> RunAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var (syncedCount, complianceFailure) = await RunTechnicianComplianceSyncAsync(ct).ConfigureAwait(false);
        var (pushedCount, payrollFailure) = await RunPayrollExportAsync(sinceUtc, ct).ConfigureAwait(false);
        var (flags, stockFailure) = await RunStockReconciliationAsync(sinceUtc, ct).ConfigureAwait(false);

        return new TotvsNightlySyncRunResult(syncedCount, pushedCount, flags, complianceFailure, payrollFailure, stockFailure);
    }

    private async Task<(int Count, Exception? Failure)> RunTechnicianComplianceSyncAsync(CancellationToken ct)
    {
        try
        {
            var records = await TotvsRetryExecutor.ExecuteWithBackoffAsync(
                innerCt => syncClient.PullTechnicianComplianceAsync(innerCt), retryOptions, timeProvider, ct).ConfigureAwait(false);

            foreach (var record in records)
            {
                await complianceRepository.UpsertAsync(record, ct).ConfigureAwait(false);
            }

            return (records.Count, null);
        }
        catch (Exception ex)
        {
            return (0, ex);
        }
    }

    private async Task<(int Count, Exception? Failure)> RunPayrollExportAsync(DateTime sinceUtc, CancellationToken ct)
    {
        try
        {
            var postings = await postingSource.GetApprovedPostingsSinceAsync(sinceUtc, ct).ConfigureAwait(false);
            var (lines, _) = TotvsPayrollExportMapper.Map(postings);

            await TotvsRetryExecutor.ExecuteWithBackoffAsync(
                async innerCt => { await syncClient.PushPayrollExportAsync(lines, innerCt).ConfigureAwait(false); return true; },
                retryOptions, timeProvider, ct).ConfigureAwait(false);

            return (lines.Count, null);
        }
        catch (Exception ex)
        {
            return (0, ex);
        }
    }

    private async Task<(IReadOnlyList<MaterialShrinkageFlag> Flags, Exception? Failure)> RunStockReconciliationAsync(DateTime sinceUtc, CancellationToken ct)
    {
        try
        {
            var issuances = await TotvsRetryExecutor.ExecuteWithBackoffAsync(
                innerCt => syncClient.PullWarehouseStockIssuancesAsync(sinceUtc, innerCt), retryOptions, timeProvider, ct).ConfigureAwait(false);
            var consumption = await consumptionSource.GetConsumptionSinceAsync(sinceUtc, ct).ConfigureAwait(false);

            var flags = MaterialConsumptionReconciler.Reconcile(issuances, consumption, DefaultShrinkageToleranceFraction);
            return (flags, null);
        }
        catch (Exception ex)
        {
            return ([], ex);
        }
    }
}
