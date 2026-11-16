namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>One field-reported material-consumption claim, for reconciliation against Sienge purchase orders (XD01-254).</summary>
public sealed record FieldConsumptionRecord(
    string CostCenterCode,
    string MaterialCode,
    decimal QuantityConsumed);
