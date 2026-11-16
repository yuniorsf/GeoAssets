namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// Persistence port for the latest <see cref="TechnicianComplianceRecord"/> per technician,
/// as populated by the nightly Totvs Protheus pull (XD01-253). A technician with no record at
/// all (never synced) is treated as unknown, not non-compliant — see
/// <c>TechnicianComplianceTransitionGuard</c>.
/// </summary>
public interface ITechnicianComplianceRepository
{
    Task<TechnicianComplianceRecord?> GetAsync(string technicianId, CancellationToken ct = default);

    /// <summary>Replaces the stored snapshot for this technician with the latest pulled one.</summary>
    Task UpsertAsync(TechnicianComplianceRecord record, CancellationToken ct = default);
}
