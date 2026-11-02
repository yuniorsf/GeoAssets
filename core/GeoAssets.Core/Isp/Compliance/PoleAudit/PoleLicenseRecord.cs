namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// The ISP's own SCM (Serviço de Comunicação Multimídia) license data for a pole attachment,
/// linked for injunction defense. No ANATEL/SCM registry integration ticket exists yet in this
/// project, so this record is caller-supplied rather than fetched/validated against a live
/// registry — the same scope-cut already applied to XD01-280's BSS-reconcile endpoint and
/// XD01-283's direct-optical-distance fallback (caller-supplied value pending real integration).
/// </summary>
public sealed record PoleLicenseRecord(
    string PoleFeatureId,
    string ScmLicenseNumber,
    string LicenseHolderName,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc);
