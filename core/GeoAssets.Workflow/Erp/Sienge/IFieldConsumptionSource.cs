namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>Host-side source of field-reported material consumption for purchase-order reconciliation (XD01-254).</summary>
public interface IFieldConsumptionSource
{
    Task<IReadOnlyList<FieldConsumptionRecord>> GetConsumptionSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}
