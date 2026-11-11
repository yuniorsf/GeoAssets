namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// One field-consumed quantity against a material code, for <c>MaterialConsumptionReconciler</c>
/// to compare against <see cref="WarehouseStockIssuanceRecord"/>. The host layer builds these
/// from whatever ledger/BoQ data it has (see <see cref="WarehouseStockIssuanceRecord"/>'s UsCode
/// caveat) — this type intentionally carries no dependency on
/// <c>GeoAssets.Workflow.Contractors</c>, so the reconciliation logic itself stays agnostic to
/// where "consumption" data comes from.
/// </summary>
public sealed record FieldMaterialConsumptionRecord(
    string MaterialCode,
    string? ServiceOrderId,
    decimal QuantityConsumed);
