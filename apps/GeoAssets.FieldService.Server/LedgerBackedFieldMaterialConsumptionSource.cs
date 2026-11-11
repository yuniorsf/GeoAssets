using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Erp.Totvs;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// <see cref="IFieldMaterialConsumptionSource"/> backed by <see cref="IContractorUsLedgerRepository"/>
/// (XD01-253): this codebase has no raw-material SKU catalog distinct from the Unidade de
/// Serviço billing codes, so each ledger entry's <see cref="ContractorUsLedgerEntry.UsCode"/> and
/// <see cref="ContractorUsLedgerEntry.Quantity"/> are the closest available proxy for "material
/// consumed in the field" — see <see cref="WarehouseStockIssuanceRecord"/>'s doc comment for the
/// same caveat from the issuance side.
/// </summary>
internal sealed class LedgerBackedFieldMaterialConsumptionSource(
    IContractorUsLedgerRepository ledgerRepository,
    TimeProvider timeProvider) : IFieldMaterialConsumptionSource
{
    public async Task<IReadOnlyList<FieldMaterialConsumptionRecord>> GetConsumptionSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var entries = await ledgerRepository.GetByPeriodAsync(sinceUtc, timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);

        return entries
            .Select(e => new FieldMaterialConsumptionRecord(e.UsCode, e.ServiceOrderId, e.Quantity))
            .ToList();
    }
}
