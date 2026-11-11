using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Erp.Sienge;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// <see cref="IHaulingTicketSource"/> backed by <see cref="IContractorUsLedgerRepository"/>
/// (XD01-254): hauling-ticket claims are ledger entries whose
/// <see cref="ContractorUsLedgerEntry.UsCode"/> falls in <see cref="SiengeUsCodeClassification.HaulingTicketUsCodes"/>
/// — see <see cref="LedgerBackedMachineHourSource"/>'s doc comment for the shared rationale.
/// </summary>
internal sealed class LedgerBackedHaulingTicketSource(
    IContractorUsLedgerRepository ledgerRepository,
    SiengeUsCodeClassification classification,
    TimeProvider timeProvider) : IHaulingTicketSource
{
    public async Task<IReadOnlyList<HaulingTicketRecord>> GetHaulingTicketsSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var entries = await ledgerRepository.GetByPeriodAsync(sinceUtc, timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);

        return entries
            .Where(e => classification.HaulingTicketUsCodes.Contains(e.UsCode) && e.ServiceOrderId is not null)
            .Select(e => new HaulingTicketRecord(e.ServiceOrderId!, e.UsCode, e.Quantity, e.CreatedAt))
            .ToList();
    }
}
