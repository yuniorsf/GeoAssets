namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>One Sienge purchase order (e.g. municipal asphalt) for a cost center (XD01-254).</summary>
public sealed record SiengePurchaseOrderRecord(
    string PurchaseOrderNumber,
    string CostCenterCode,
    string MaterialCode,
    decimal QuantityPurchased);
