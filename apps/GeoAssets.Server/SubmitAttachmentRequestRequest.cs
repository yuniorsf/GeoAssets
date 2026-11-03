namespace GeoAssets.Server;

/// <summary>Request body for <c>POST /api/isp/tenant-portal/attachment-requests</c> (XD01-269).</summary>
public sealed record SubmitAttachmentRequestRequest(string PoleFeatureId, string Description);
