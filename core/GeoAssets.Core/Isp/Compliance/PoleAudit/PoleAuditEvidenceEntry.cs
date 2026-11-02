namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// One HMAC-sealed field photo (XD01-278's <c>EvidenceVerificationService</c>) linked to the pole
/// it audits. <see cref="CapturedAtUtc"/>/<see cref="Latitude"/>/<see cref="Longitude"/> are read
/// back out of the photo's own verified EXIF signature descriptor at ingestion time, not supplied
/// by the caller — so they can't drift from what the seal actually attests to.
/// </summary>
public sealed record PoleAuditEvidenceEntry(
    string PoleFeatureId,
    PoleAuditEvidenceKind Kind,
    DateTimeOffset CapturedAtUtc,
    double Latitude,
    double Longitude,
    byte[] SignedJpegBytes);
