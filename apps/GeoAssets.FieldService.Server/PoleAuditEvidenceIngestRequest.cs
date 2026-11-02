using GeoAssets.Core.Isp.Compliance.PoleAudit;

namespace GeoAssets.FieldService.Server;

internal sealed record PoleAuditEvidenceIngestRequest(byte[] SignedJpegBytes, string TenantSecret, PoleAuditEvidenceKind Kind);
