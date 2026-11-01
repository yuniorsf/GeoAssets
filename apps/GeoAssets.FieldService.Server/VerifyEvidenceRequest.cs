namespace GeoAssets.FieldService.Server;

/// <summary>
/// XD01-278's server-side evidence verification request. <see cref="TenantSecret"/> is
/// caller-supplied rather than resolved server-side from a tenant-keyed secret store — the same
/// gap <c>MauiEvidenceCaptureGateway.CaptureAsync</c> already flags ("no real secret-provisioning
/// mechanism exists yet"), kept consistent here rather than inventing a different, parallel
/// resolution scheme for this one endpoint.
/// </summary>
internal sealed record VerifyEvidenceRequest(byte[] SignedJpegBytes, string TenantSecret);
