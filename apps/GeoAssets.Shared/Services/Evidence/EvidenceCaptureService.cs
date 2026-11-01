using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Shared.Services.Evidence;

public sealed class EvidenceCaptureService(IEvidenceWatermarkComposer watermarkComposer) : IEvidenceCaptureService
{
    public EvidenceCaptureResult Capture(byte[] rawJpegBytes, GpsFix fix, EvidenceCaptureContext context, string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(rawJpegBytes);
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentNullException.ThrowIfNull(context);

        if (!GpsAccuracyGate.IsAcceptable(fix))
        {
            throw new EvidenceCaptureRejectedException(fix.IsFromMockProvider
                ? "Location reports a mock provider; evidence capture is blocked."
                : $"GPS horizontal accuracy {fix.HorizontalAccuracyMeters:F1}m exceeds the {GpsAccuracyGate.MaxHorizontalAccuracyMeters:F0}m limit; evidence capture is blocked.");
        }

        var watermarked = watermarkComposer.BurnWatermark(rawJpegBytes, EvidenceWatermarkTextFormatter.BuildLines(context, fix));
        var signature = EvidenceHmacSigner.ComputeSignature(watermarked, fix.Latitude, fix.Longitude, context.CapturedAtUtc, context.OrderId, tenantSecret);

        var descriptor = new EvidenceSignatureDescriptor(signature, fix.Latitude, fix.Longitude, context.CapturedAtUtc, context.OrderId);
        var signed = JpegExifImageDescriptionCodec.WriteImageDescription(watermarked, descriptor.ToDescriptorString());

        return new EvidenceCaptureResult(signed, signature);
    }

    public EvidenceVerificationResult Verify(byte[] signedJpegBytes, string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(signedJpegBytes);

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
