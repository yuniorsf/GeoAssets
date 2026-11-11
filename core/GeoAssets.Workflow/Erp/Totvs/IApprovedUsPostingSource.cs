namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>Host-side source of approved US ledger postings for the payroll export (XD01-253).</summary>
public interface IApprovedUsPostingSource
{
    Task<IReadOnlyList<ApprovedUsPosting>> GetApprovedPostingsSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}
