using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Erp.Totvs;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// <see cref="IApprovedUsPostingSource"/> backed by <see cref="IContractorUsLedgerRepository"/>
/// (XD01-253): this host's US ledger only ever persists entries for orders that already reached
/// the Inspected transition (XD01-228), so every entry in the period is, by construction,
/// already "approved" — there's no separate pending/rejected ledger status to filter on here.
/// The technician is resolved as the order's <see cref="IServiceOrder.AssignedTo"/>; an entry
/// whose order can't be found, or has no assignee, maps to a null
/// <see cref="ApprovedUsPosting.TechnicianId"/> (<c>TotvsPayrollExportMapper</c> skips it).
/// </summary>
internal sealed class LedgerBackedApprovedUsPostingSource(
    IContractorUsLedgerRepository ledgerRepository,
    IServiceOrderRepository orderRepository,
    TimeProvider timeProvider) : IApprovedUsPostingSource
{
    public async Task<IReadOnlyList<ApprovedUsPosting>> GetApprovedPostingsSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var entries = await ledgerRepository.GetByPeriodAsync(sinceUtc, timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);

        var postings = new List<ApprovedUsPosting>(entries.Count);
        foreach (var entry in entries)
        {
            string? technicianId = null;
            if (!string.IsNullOrWhiteSpace(entry.ServiceOrderId))
            {
                var order = await orderRepository.GetByIdAsync(entry.ServiceOrderId, ct).ConfigureAwait(false);
                technicianId = order?.AssignedTo;
            }

            postings.Add(new ApprovedUsPosting(
                entry.ServiceOrderId ?? string.Empty,
                technicianId,
                entry.UsCode,
                entry.Quantity,
                entry.UnitValue,
                entry.CreatedAt));
        }

        return postings;
    }
}
