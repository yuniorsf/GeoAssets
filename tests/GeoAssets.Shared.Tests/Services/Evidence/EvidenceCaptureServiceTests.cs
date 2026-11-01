using FluentAssertions;
using GeoAssets.Shared.Services.Evidence;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Evidence;

/// <summary>
/// Exercises <see cref="EvidenceCaptureService"/>'s full gate/watermark/sign pipeline with a
/// hand-written fake <see cref="IEvidenceWatermarkComposer"/> standing in for the real
/// SkiaSharp-backed MAUI implementation — no mocking library, consistent with this codebase's
/// precedent (see <c>MauiSyncGatewayTests</c>).
/// </summary>
public class EvidenceCaptureServiceTests
{
    private sealed class FakeWatermarkComposer : IEvidenceWatermarkComposer
    {
        public byte[] BurnWatermark(byte[] jpegBytes, IReadOnlyList<string> lines) => [.. jpegBytes, 0xAA]; // marks that it ran
    }

    private static readonly EvidenceCaptureContext Context = new(
        OrderId: "order-1", TechnicianId: "tech-7", AssetTag: "US-EL-014",
        CapturedAtUtc: new DateTimeOffset(2026, 11, 1, 14, 32, 0, TimeSpan.Zero), CompassHeadingDegrees: 90);

    private static byte[] FakeJpeg() => [0xFF, 0xD8, 0x01, 0x02, 0x03, 0xFF, 0xD9];

    private static EvidenceCaptureService BuildService() => new(new FakeWatermarkComposer());

    [Fact]
    public void Capture_AcceptableFix_ProducesVerifiableResult()
    {
        var service = BuildService();
        var fix = new GpsFix(-23.55052, -46.633309, 3.2, IsFromMockProvider: false);

        var result = service.Capture(FakeJpeg(), fix, Context, "tenant-secret");
        var verification = service.Verify(result.SignedJpegBytes, "tenant-secret");

        verification.IsValid.Should().BeTrue();
        verification.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Capture_LowAccuracyFix_ThrowsAndNeverProducesAnImage()
    {
        var service = BuildService();
        var fix = new GpsFix(0, 0, HorizontalAccuracyMeters: 25, IsFromMockProvider: false);

        var act = () => service.Capture(FakeJpeg(), fix, Context, "tenant-secret");

        act.Should().Throw<EvidenceCaptureRejectedException>();
    }

    [Fact]
    public void Capture_MockProviderFix_ThrowsRegardlessOfAccuracy()
    {
        var service = BuildService();
        var fix = new GpsFix(0, 0, HorizontalAccuracyMeters: 0.1, IsFromMockProvider: true);

        var act = () => service.Capture(FakeJpeg(), fix, Context, "tenant-secret");

        act.Should().Throw<EvidenceCaptureRejectedException>();
    }

    [Fact]
    public void Verify_TamperedImageBytes_IsRejected()
    {
        var service = BuildService();
        var fix = new GpsFix(-23.55052, -46.633309, 3.2, IsFromMockProvider: false);
        var result = service.Capture(FakeJpeg(), fix, Context, "tenant-secret");

        var tampered = (byte[])result.SignedJpegBytes.Clone();
        tampered[^1] ^= 0xFF; // flip the trailing pixel-data byte, well outside the EXIF segment

        var verification = service.Verify(tampered, "tenant-secret");

        verification.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongTenantSecret_IsRejected()
    {
        var service = BuildService();
        var fix = new GpsFix(-23.55052, -46.633309, 3.2, IsFromMockProvider: false);
        var result = service.Capture(FakeJpeg(), fix, Context, "tenant-secret");

        var verification = service.Verify(result.SignedJpegBytes, "wrong-secret");

        verification.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_ImageWithNoSignature_IsRejected()
    {
        var service = BuildService();

        var verification = service.Verify(FakeJpeg(), "tenant-secret");

        verification.IsValid.Should().BeFalse();
        verification.FailureReason.Should().NotBeNullOrEmpty();
    }
}
