namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// Outcome of one <c>TotvsNightlySyncOrchestrator.RunAsync</c> pass. Every operation's
/// exception (after its own retries are exhausted) is captured here rather than left to
/// propagate and abort the other two operations — XD01-253's "surfaced, not silently dropped" AC.
/// </summary>
public sealed record TotvsNightlySyncRunResult(
    int TechnicianComplianceRecordsSynced,
    int PayrollExportLinesPushed,
    IReadOnlyList<MaterialShrinkageFlag> ShrinkageFlags,
    Exception? TechnicianComplianceFailure,
    Exception? PayrollExportFailure,
    Exception? StockReconciliationFailure)
{
    public bool AllOperationsSucceeded =>
        TechnicianComplianceFailure is null && PayrollExportFailure is null && StockReconciliationFailure is null;
}
