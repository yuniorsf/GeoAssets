namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// One line of the nightly payroll export, in the shape Totvs Protheus's SIGAGPE payroll module
/// expects for production-bonus calculation (XD01-253). Mapped from an approved
/// (ledger-committed, i.e. already past the order's Inspected transition — XD01-228) US
/// (Unidade de Serviço) posting plus the technician assigned to that posting's order.
/// </summary>
public sealed record ProtheusPayrollExportLine(
    string TechnicianId,
    string ServiceOrderId,
    string UsCode,
    decimal Quantity,
    decimal UnitValue,
    decimal TotalValue,
    DateTime PostedAtUtc);
