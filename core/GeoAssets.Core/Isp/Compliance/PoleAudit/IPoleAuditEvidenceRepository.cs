namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// Links one pole's photo evidence, SCM license data, and generated compliance dossiers into a
/// single queryable record, used to contest arbitrary-cut notices within the 30-day statutory
/// window (XD01-285).
/// </summary>
public interface IPoleAuditEvidenceRepository
{
    void AddEvidence(PoleAuditEvidenceEntry entry);

    void AddLicenseRecord(PoleLicenseRecord record);

    void AddDossierRecord(PoleDossierRecord record);

    /// <summary>
    /// Everything needed for an injunction defense for <paramref name="poleFeatureId"/> in one
    /// call (AC2). Evidence captured before <paramref name="validInstallationTimestampUtc"/> is
    /// excluded (AC3) — it predates the audit establishing the installation was compliant, so it
    /// cannot be used to prove the pole's current compliant status. License records and dossier
    /// records are not time-filtered: a license or dossier doesn't become stale evidence just
    /// because it was issued/generated before the audit the same way a photo does.
    /// </summary>
    PoleDefenseBundle BuildDefenseBundle(string poleFeatureId, DateTimeOffset validInstallationTimestampUtc);
}
