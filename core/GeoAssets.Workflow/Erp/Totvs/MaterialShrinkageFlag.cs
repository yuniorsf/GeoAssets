namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>A material code/order whose issued quantity exceeds consumed quantity by more than the configured tolerance.</summary>
public sealed record MaterialShrinkageFlag(
    string MaterialCode,
    string? ServiceOrderId,
    decimal QuantityIssued,
    decimal QuantityConsumed,
    decimal ShrinkageQuantity,
    decimal ShrinkageFraction);
