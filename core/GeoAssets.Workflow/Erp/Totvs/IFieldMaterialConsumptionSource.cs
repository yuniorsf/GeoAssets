namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>Host-side source of field material consumption for shrinkage reconciliation (XD01-253).</summary>
public interface IFieldMaterialConsumptionSource
{
    Task<IReadOnlyList<FieldMaterialConsumptionRecord>> GetConsumptionSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}
