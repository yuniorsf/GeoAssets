using GeoAssets.NeutralOperator.Domain.Compliance.PoleAudit;

namespace GeoAssets.FieldService.Server;

internal sealed record PoleAuditEvidenceIngestRequest(byte[] SignedJpegBytes, string TenantSecret, PoleAuditEvidenceKind Kind);
