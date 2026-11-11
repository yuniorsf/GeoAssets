namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// Maps approved ledger postings into Protheus SIGAGPE payroll export lines (XD01-253). Pure —
/// the host supplies already-approved postings, each already carrying its resolved technician
/// (this codebase's US ledger only ever persists entries for orders that reached the Inspected
/// transition, XD01-228, so every <see cref="ApprovedUsPosting"/> the caller supplies is by
/// construction already "approved" — there's no separate pending/rejected ledger state to filter
/// here). A posting with no resolved technician is skipped (can't export to payroll without a
/// payee) and returned separately so the caller can surface it rather than it silently vanishing.
/// </summary>
public static class TotvsPayrollExportMapper
{
    public static (IReadOnlyList<ProtheusPayrollExportLine> Lines, IReadOnlyList<ApprovedUsPosting> Unresolved) Map(
        IReadOnlyList<ApprovedUsPosting> postings)
    {
        ArgumentNullException.ThrowIfNull(postings);

        var lines = new List<ProtheusPayrollExportLine>();
        var unresolved = new List<ApprovedUsPosting>();

        foreach (var posting in postings)
        {
            if (string.IsNullOrWhiteSpace(posting.TechnicianId))
            {
                unresolved.Add(posting);
                continue;
            }

            lines.Add(new ProtheusPayrollExportLine(
                posting.TechnicianId,
                posting.ServiceOrderId,
                posting.UsCode,
                posting.Quantity,
                posting.UnitValue,
                posting.Quantity * posting.UnitValue,
                posting.PostedAtUtc));
        }

        return (lines, unresolved);
    }
}
