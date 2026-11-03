namespace GeoAssets.Server;

/// <summary>Request body for <c>POST /api/isp/tenant-portal/flagged-notices/{id}/dispute</c> (XD01-269).</summary>
public sealed record SubmitDisputeRequest(string EvidenceReference);
