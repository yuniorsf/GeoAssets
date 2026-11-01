using GeoAssets.Core.Services.Evidence;

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

    /// <summary>Delegates to <see cref="EvidenceVerificationService.Verify"/> (XD01-278) — moved to <c>GeoAssets.Core</c> so a non-Blazor host can call it too.</summary>
    public EvidenceVerificationResult Verify(byte[] signedJpegBytes, string tenantSecret) =>
        EvidenceVerificationService.Verify(signedJpegBytes, tenantSecret);
}
