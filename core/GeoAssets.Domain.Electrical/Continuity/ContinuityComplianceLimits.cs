namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>
/// The DIC/FIC/DMIC limits a consumer unit must stay within, per PRODIST Módulo 8. ANEEL
/// publishes these per distributor/region/consumer grouping ("conjunto") and revises them
/// periodically — deliberately not a hardcoded table here; source from the operator's own
/// current regulatory reference data per unit.
/// </summary>
public sealed record ContinuityComplianceLimits(TimeSpan DicLimit, int FicLimit, TimeSpan DmicLimit);
