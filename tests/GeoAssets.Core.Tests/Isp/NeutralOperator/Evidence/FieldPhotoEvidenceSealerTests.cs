using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Evidence;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Evidence;

public class FieldPhotoEvidenceSealerTests
{
    private const string DeviceSecret = "unit-test-device-secret";

    private static readonly FieldPhotoEvidenceMetadata Metadata = new(
        InspectorId: "INSP-001",
        PoleId: "POLE-123",
        UtcTimestamp: new DateTimeOffset(2026, 3, 1, 14, 30, 0, TimeSpan.Zero),
        Latitude: -22.906847,
        Longitude: -43.172897,
        CompassHeadingDegrees: 275.5,
        OrderId: "ORDER-999");

    private static byte[] CreateSyntheticImageBytes() => [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public void ComputeDigest_SameInputsTwice_ProducesIdenticalDigest()
    {
        var image = CreateSyntheticImageBytes();

        var first = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);
        var second = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);

        first.Should().Be(second);
    }

    [Fact]
    public void VerifyDigest_LegitimatePhoto_Verifies()
    {
        var image = CreateSyntheticImageBytes();
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);

        FieldPhotoEvidenceSealer.VerifyDigest(image, Metadata, digest, DeviceSecret).Should().BeTrue();
    }

    [Fact]
    public void VerifyDigest_TamperedImageByte_FailsVerification()
    {
        var image = CreateSyntheticImageBytes();
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);

        var tampered = (byte[])image.Clone();
        tampered[^1] ^= 0xFF;

        FieldPhotoEvidenceSealer.VerifyDigest(tampered, Metadata, digest, DeviceSecret).Should().BeFalse();
    }

    [Fact]
    public void VerifyDigest_TamperedMetadataLatitude_FailsVerification()
    {
        var image = CreateSyntheticImageBytes();
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);

        var tamperedMetadata = Metadata with { Latitude = Metadata.Latitude + 0.0001 };

        FieldPhotoEvidenceSealer.VerifyDigest(image, tamperedMetadata, digest, DeviceSecret).Should().BeFalse();
    }

    [Fact]
    public void VerifyDigest_WrongDeviceSecret_FailsVerification()
    {
        var image = CreateSyntheticImageBytes();
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);

        FieldPhotoEvidenceSealer.VerifyDigest(image, Metadata, digest, "a-different-secret").Should().BeFalse();
    }

    [Fact]
    public void VerifyDigest_MalformedBase64Digest_ReturnsFalseRatherThanThrowing()
    {
        var image = CreateSyntheticImageBytes();

        FieldPhotoEvidenceSealer.VerifyDigest(image, Metadata, "not-valid-base64!!", DeviceSecret).Should().BeFalse();
    }

    [Fact]
    public void VerifyDigest_EmptyClaimedDigest_ReturnsFalse()
    {
        var image = CreateSyntheticImageBytes();

        FieldPhotoEvidenceSealer.VerifyDigest(image, Metadata, string.Empty, DeviceSecret).Should().BeFalse();
    }
}
