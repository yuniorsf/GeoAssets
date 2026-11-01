using FluentAssertions;
using GeoAssets.Core.Services.Evidence;
using Xunit;

namespace GeoAssets.Core.Tests.Services.Evidence;

/// <summary>
/// XD01-278's AC: "HMAC signature is verifiable server-side on ingestion; a tampered payload
/// fails verification." Exercises <see cref="EvidenceVerificationService"/> directly — the
/// server-callable path, with no <c>IEvidenceWatermarkComposer</c>/MAUI dependency at all,
/// unlike <c>GeoAssets.Shared.Tests.Services.Evidence.EvidenceCaptureServiceTests</c>'s
/// full capture-then-verify round trip.
/// </summary>
public class EvidenceVerificationServiceTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 11, 1, 14, 32, 0, TimeSpan.Zero);

    private static byte[] FakeJpeg() => [0xFF, 0xD8, 0x01, 0x02, 0x03, 0xFF, 0xD9];

    private static byte[] BuildSignedJpeg(byte[] rawJpeg, double latitude, double longitude, DateTimeOffset capturedAtUtc, string orderId, string tenantSecret)
    {
        var signature = EvidenceHmacSigner.ComputeSignature(rawJpeg, latitude, longitude, capturedAtUtc, orderId, tenantSecret);
        var descriptor = new EvidenceSignatureDescriptor(signature, latitude, longitude, capturedAtUtc, orderId);
        return JpegExifImageDescriptionCodec.WriteImageDescription(rawJpeg, descriptor.ToDescriptorString());
    }

    [Fact]
    public void Verify_UntamperedSignedPayload_IsValid()
    {
        var signed = BuildSignedJpeg(FakeJpeg(), -23.55052, -46.633309, CapturedAt, "order-1", "tenant-secret");

        var result = EvidenceVerificationService.Verify(signed, "tenant-secret");

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Verify_ImageBytesTamperedAfterSigning_FailsVerification()
    {
        var signed = BuildSignedJpeg(FakeJpeg(), -23.55052, -46.633309, CapturedAt, "order-1", "tenant-secret");
        signed[^1] ^= 0xFF; // flips the EOI byte at the end of the original image bytes

        var result = EvidenceVerificationService.Verify(signed, "tenant-secret");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Verify_WrongTenantSecret_FailsVerification()
    {
        var signed = BuildSignedJpeg(FakeJpeg(), -23.55052, -46.633309, CapturedAt, "order-1", "tenant-secret");

        var result = EvidenceVerificationService.Verify(signed, "wrong-secret");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_NoEvidenceSignaturePresent_FailsWithClearReason()
    {
        var result = EvidenceVerificationService.Verify(FakeJpeg(), "tenant-secret");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("No evidence signature");
    }
}
