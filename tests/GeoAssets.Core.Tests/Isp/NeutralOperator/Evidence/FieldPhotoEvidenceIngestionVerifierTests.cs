using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Evidence;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Evidence;

public class FieldPhotoEvidenceIngestionVerifierTests
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
    public void Verify_LegitimateSubmission_ReturnsVerified()
    {
        var image = CreateSyntheticImageBytes();
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);
        var submission = new FieldPhotoEvidenceSubmission(image, Metadata, digest);

        FieldPhotoEvidenceIngestionVerifier.Verify(submission, DeviceSecret)
            .Should().Be(FieldPhotoEvidenceVerificationResult.Verified);
    }

    [Fact]
    public void Verify_MissingDigest_ReturnsDigestMissing()
    {
        var submission = new FieldPhotoEvidenceSubmission(CreateSyntheticImageBytes(), Metadata, ClaimedDigestBase64: null);

        FieldPhotoEvidenceIngestionVerifier.Verify(submission, DeviceSecret)
            .Should().Be(FieldPhotoEvidenceVerificationResult.DigestMissing);
    }

    [Fact]
    public void Verify_TamperedImage_ReturnsDigestMismatch()
    {
        var image = CreateSyntheticImageBytes();
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);
        var tampered = (byte[])image.Clone();
        tampered[0] ^= 0xFF;
        var submission = new FieldPhotoEvidenceSubmission(tampered, Metadata, digest);

        FieldPhotoEvidenceIngestionVerifier.Verify(submission, DeviceSecret)
            .Should().Be(FieldPhotoEvidenceVerificationResult.DigestMismatch);
    }

    [Fact]
    public void RequiresRejection_EvidenceMandatoryAndDigestMissing_ReturnsTrue()
    {
        var submission = new FieldPhotoEvidenceSubmission(CreateSyntheticImageBytes(), Metadata, ClaimedDigestBase64: null);

        FieldPhotoEvidenceIngestionVerifier.RequiresRejection(submission, DeviceSecret, evidenceMandatory: true)
            .Should().BeTrue();
    }

    [Fact]
    public void RequiresRejection_EvidenceNotMandatoryAndDigestMissing_ReturnsFalse()
    {
        var submission = new FieldPhotoEvidenceSubmission(CreateSyntheticImageBytes(), Metadata, ClaimedDigestBase64: null);

        FieldPhotoEvidenceIngestionVerifier.RequiresRejection(submission, DeviceSecret, evidenceMandatory: false)
            .Should().BeFalse();
    }

    [Fact]
    public void RequiresRejection_ValidDigestAndMandatory_ReturnsFalse()
    {
        var image = CreateSyntheticImageBytes();
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(image, Metadata, DeviceSecret);
        var submission = new FieldPhotoEvidenceSubmission(image, Metadata, digest);

        FieldPhotoEvidenceIngestionVerifier.RequiresRejection(submission, DeviceSecret, evidenceMandatory: true)
            .Should().BeFalse();
    }
}
