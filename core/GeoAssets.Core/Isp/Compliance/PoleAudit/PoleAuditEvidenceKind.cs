namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// The three photo angles XD01-285's AC narrative requires per audited pole: the attachment
/// anchor, the yellow non-conformity tag (if any), and the 500mm minimum-separation band position.
/// </summary>
public enum PoleAuditEvidenceKind
{
    AnchorPhoto,
    YellowTagPhoto,
    BandPosition500mmPhoto
}
