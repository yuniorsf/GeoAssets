namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// One warehouse-to-field material issuance, as pulled from Totvs Protheus (XD01-253). The
/// material code is the same key space as <see cref="ProtheusPayrollExportLine.UsCode"/> — this
/// codebase has no separate raw-material SKU catalog distinct from the Unidade de Serviço
/// billing codes already used throughout <c>GeoAssets.Workflow.Contractors</c>, so UsCode is the
/// only consumption proxy available for shrinkage reconciliation. See
/// <c>MaterialConsumptionReconciler</c>.
/// </summary>
public sealed record WarehouseStockIssuanceRecord(
    string MaterialCode,
    string WarehouseId,
    decimal QuantityIssued,
    string? ServiceOrderId,
    DateTime IssuedAtUtc);
