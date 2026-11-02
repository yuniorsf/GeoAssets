namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// Everything needed for an injunction defense for one pole, in one call (XD01-285's AC2) — the
/// result of <see cref="IPoleAuditEvidenceRepository.BuildDefenseBundle"/>.
/// </summary>
public sealed record PoleDefenseBundle(
    string PoleFeatureId,
    IReadOnlyList<PoleAuditEvidenceEntry> Evidence,
    IReadOnlyList<PoleLicenseRecord> LicenseRecords,
    IReadOnlyList<PoleDossierRecord> DossierRecords);
