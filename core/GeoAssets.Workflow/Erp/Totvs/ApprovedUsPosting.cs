namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// One approved US (Unidade de Serviço) ledger posting to map into a
/// <see cref="ProtheusPayrollExportLine"/>. <paramref name="TechnicianId"/> is null when the
/// order has no resolvable assignee (typically <c>IServiceOrder.AssignedTo</c>) — the host
/// resolves it when building this record, since only the host knows how to look orders up.
/// </summary>
public sealed record ApprovedUsPosting(string ServiceOrderId, string? TechnicianId, string UsCode, decimal Quantity, decimal UnitValue, DateTime PostedAtUtc);
