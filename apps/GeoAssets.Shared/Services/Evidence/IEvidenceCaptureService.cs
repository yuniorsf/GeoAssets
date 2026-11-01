namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// XD01-244's evidence-capture pipeline, shared infrastructure the photo gating engine
/// (XD01-246) composes with rather than reimplementing: gates a raw captured photo on GPS
/// accuracy, burns the required watermark into its pixels, and seals it with a per-tenant
/// HMAC-SHA256 signature in its EXIF <c>ImageDescription</c>.
/// </summary>
public interface IEvidenceCaptureService
{
    /// <summary>Throws <see cref="EvidenceCaptureRejectedException"/> if <paramref name="fix"/> fails <see cref="GpsAccuracyGate"/>.</summary>
    EvidenceCaptureResult Capture(byte[] rawJpegBytes, GpsFix fix, EvidenceCaptureContext context, string tenantSecret);

    /// <summary>
    /// Re-derives the signature from <paramref name="signedJpegBytes"/>' own EXIF
    /// <c>ImageDescription</c> and pixel content and compares it against what is stored there —
    /// fails if any byte of the image (or the signed metadata) was altered after capture.
    /// </summary>
    EvidenceVerificationResult Verify(byte[] signedJpegBytes, string tenantSecret);
}
