using FluentAssertions;
using GeoAssets.Shared.Services.Evidence;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Evidence;

public class JpegExifImageDescriptionCodecTests
{
    private static byte[] FakeJpeg(params byte[] body) => [0xFF, 0xD8, .. body, 0xFF, 0xD9];

    private static byte[] FakeJpegWithApp0(params byte[] body)
    {
        byte[] app0Payload = [0x4A, 0x46, 0x49, 0x46, 0x00]; // "JFIF\0"
        var app0Length = (ushort)(2 + app0Payload.Length);
        return [0xFF, 0xD8, 0xFF, 0xE0, (byte)(app0Length >> 8), (byte)app0Length, .. app0Payload, .. body, 0xFF, 0xD9];
    }

    [Theory]
    [InlineData("ab")] // 3 bytes with null terminator: inline TIFF value path
    [InlineData("GeoAssetsEvidenceV1;c2lnbmF0dXJl;-23.55052;-46.633309;2026-11-01T14:32:00.0000000+00:00;order-123")] // long: out-of-line path
    public void WriteThenRead_RoundTrips(string description)
    {
        var jpeg = FakeJpeg(0x01, 0x02, 0x03);

        var signed = JpegExifImageDescriptionCodec.WriteImageDescription(jpeg, description);
        var found = JpegExifImageDescriptionCodec.TryReadImageDescription(signed, out var read);

        found.Should().BeTrue();
        read.Should().Be(description);
    }

    [Fact]
    public void Write_InsertsAfterExistingApp0Segment()
    {
        var jpeg = FakeJpegWithApp0(0x01, 0x02, 0x03);

        var signed = JpegExifImageDescriptionCodec.WriteImageDescription(jpeg, "hello");

        // APP0 (0xFFE0) must still come first, immediately after SOI.
        signed[2].Should().Be(0xFF);
        signed[3].Should().Be(0xE0);
        JpegExifImageDescriptionCodec.TryReadImageDescription(signed, out var read).Should().BeTrue();
        read.Should().Be("hello");
    }

    [Fact]
    public void Strip_RecoversOriginalBytes()
    {
        var jpeg = FakeJpeg(0x01, 0x02, 0x03, 0x04, 0x05);

        var signed = JpegExifImageDescriptionCodec.WriteImageDescription(jpeg, "evidence-signature-payload");
        var stripped = JpegExifImageDescriptionCodec.StripImageDescriptionSegment(signed);

        stripped.Should().Equal(jpeg);
    }

    [Fact]
    public void Strip_WithNoSignatureSegment_IsNoOp()
    {
        var jpeg = FakeJpeg(0x01, 0x02, 0x03);

        var stripped = JpegExifImageDescriptionCodec.StripImageDescriptionSegment(jpeg);

        stripped.Should().Equal(jpeg);
    }

    [Fact]
    public void TryReadImageDescription_WithNoSignatureSegment_ReturnsFalse()
    {
        var jpeg = FakeJpeg(0x01, 0x02, 0x03);

        JpegExifImageDescriptionCodec.TryReadImageDescription(jpeg, out var read).Should().BeFalse();
        read.Should().BeNull();
    }

    [Fact]
    public void WriteImageDescription_WithoutSoiMarker_Throws()
    {
        byte[] notAJpeg = [0x00, 0x01, 0x02];

        var act = () => JpegExifImageDescriptionCodec.WriteImageDescription(notAJpeg, "x");

        act.Should().Throw<ArgumentException>();
    }
}
