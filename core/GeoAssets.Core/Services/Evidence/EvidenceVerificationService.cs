using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Core.Services.Evidence;

/// <summary>
/// Re-verifies a signed evidence photo's HMAC-SHA256 signature (XD01-244's capture-time
/// signing, XD01-278's AC: "HMAC signature is verifiable server-side on ingestion; a tampered
/// payload fails verification"). Pulled out of <c>GeoAssets.Shared.Services.Evidence.EvidenceCaptureService</c>
/// into <c>GeoAssets.Core</c> — the one layer every host already depends on — specifically so a
/// plain API host (e.g. <c>GeoAssets.FieldService.Server</c>, which deliberately does not
/// reference the Blazor RCL <c>GeoAssets.Shared</c>, same boundary <c>ServerWorkflowPrincipalFactory</c>'s
/// own doc comment documents) can verify evidence on ingestion without pulling in Blazor.
/// <c>EvidenceCaptureService.Verify</c> (MAUI/Shared capture flow) now delegates here rather than
/// duplicating the logic.
/// </summary>
public static class EvidenceVerificationService
{
    /// <summary>
    /// Re-derives the signature from <paramref name="signedJpegBytes"/>' own EXIF
    /// <c>ImageDescription</c> and pixel content and compares it against what is stored there —
    /// fails if any byte of the image (or the signed metadata) was altered after capture.
    /// </summary>
    public static EvidenceVerificationResult Verify(byte[] signedJpegBytes, string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(signedJpegBytes);
        ArgumentException.ThrowIfNullOrEmpty(tenantSecret);

        if (!JpegExifImageDescriptionCodec.TryReadImageDescription(signedJpegBytes, out var raw))
            return new EvidenceVerificationResult(false, "No evidence signature found in EXIF ImageDescription.");

        if (!EvidenceSignatureDescriptor.TryParse(raw, out var descriptor) || descriptor is null)
            return new EvidenceVerificationResult(false, "Evidence signature descriptor is malformed.");

        var stripped = JpegExifImageDescriptionCodec.StripImageDescriptionSegment(signedJpegBytes);
        var recomputed = EvidenceHmacSigner.ComputeSignature(
            stripped, descriptor.Latitude, descriptor.Longitude, descriptor.CapturedAtUtc, descriptor.OrderId, tenantSecret);

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recomputed), Encoding.UTF8.GetBytes(descriptor.Signature));

        return matches
            ? new EvidenceVerificationResult(true, null)
            : new EvidenceVerificationResult(false, "Signature does not match image/metadata — evidence may have been altered after capture.");
    }
}
