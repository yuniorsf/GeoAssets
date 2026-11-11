using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Erp.Sienge;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// <see cref="IMachineHourSource"/> backed by <see cref="IContractorUsLedgerRepository"/>
/// (XD01-254): equipment-hour claims are ledger entries whose
/// <see cref="ContractorUsLedgerEntry.UsCode"/> falls in <see cref="SiengeUsCodeClassification.MachineHourUsCodes"/>
/// — see that type's doc comment for why there's no dedicated equipment-hour ledger.
/// <see cref="ContractorUsLedgerEntry.ServiceOrderId"/> stands in for the Sienge cost center
/// code, the closest available correlation key in this codebase.
/// </summary>
internal sealed class LedgerBackedMachineHourSource(
    IContractorUsLedgerRepository ledgerRepository,
    SiengeUsCodeClassification classification,
    TimeProvider timeProvider) : IMachineHourSource
{
    public async Task<IReadOnlyList<MachineHourRecord>> GetMachineHoursSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var entries = await ledgerRepository.GetByPeriodAsync(sinceUtc, timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);

        return entries
            .Where(e => classification.MachineHourUsCodes.Contains(e.UsCode) && e.ServiceOrderId is not null)
            .Select(e => new MachineHourRecord(e.ServiceOrderId!, e.UsCode, e.Quantity, e.CreatedAt))
            .ToList();
    }
}
