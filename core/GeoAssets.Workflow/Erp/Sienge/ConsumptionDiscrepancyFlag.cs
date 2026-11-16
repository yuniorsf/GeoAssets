namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// A (<see cref="CostCenterCode"/>, <see cref="MaterialCode"/>) pair where field-reported
/// consumption diverges from the Sienge purchase order beyond tolerance (XD01-254's
/// "reconciliation report flags discrepancies" AC).
/// </summary>
public sealed record ConsumptionDiscrepancyFlag(
    string CostCenterCode,
    string MaterialCode,
    decimal QuantityPurchased,
    decimal QuantityConsumed,
    decimal DiscrepancyQuantity,
    decimal DiscrepancyFraction);
