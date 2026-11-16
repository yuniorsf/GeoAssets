using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Erp.Sienge;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// <see cref="IFieldConsumptionSource"/> backed by <see cref="IContractorUsLedgerRepository"/>
/// (XD01-254): field-reported material consumption (e.g. asphalt) is ledger entries whose
/// <see cref="ContractorUsLedgerEntry.UsCode"/> falls in <see cref="SiengeUsCodeClassification.MaterialConsumptionUsCodes"/>
/// — see <see cref="LedgerBackedMachineHourSource"/>'s doc comment for the shared rationale.
/// </summary>
internal sealed class LedgerBackedFieldConsumptionSource(
    IContractorUsLedgerRepository ledgerRepository,
    SiengeUsCodeClassification classification,
    TimeProvider timeProvider) : IFieldConsumptionSource
{
    public async Task<IReadOnlyList<FieldConsumptionRecord>> GetConsumptionSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var entries = await ledgerRepository.GetByPeriodAsync(sinceUtc, timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);

        return entries
            .Where(e => classification.MaterialConsumptionUsCodes.Contains(e.UsCode) && e.ServiceOrderId is not null)
            .Select(e => new FieldConsumptionRecord(e.ServiceOrderId!, e.UsCode, e.Quantity))
            .ToList();
    }
}
