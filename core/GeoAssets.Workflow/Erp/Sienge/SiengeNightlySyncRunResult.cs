namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// Outcome of one <see cref="SiengeNightlySyncOrchestrator.RunAsync"/> pass. Each operation's
/// exception (after its own retries are exhausted) is captured here rather than left to
/// propagate and abort the other — matching the Totvs connector's "surfaced, not silently
/// dropped" pattern.
/// </summary>
public sealed record SiengeNightlySyncRunResult(
    int CostCenterPostingLinesPushed,
    IReadOnlyList<ConsumptionDiscrepancyFlag> DiscrepancyFlags,
    Exception? CostCenterPostingFailure,
    Exception? ReconciliationFailure)
{
    public bool AllOperationsSucceeded => CostCenterPostingFailure is null && ReconciliationFailure is null;
}
