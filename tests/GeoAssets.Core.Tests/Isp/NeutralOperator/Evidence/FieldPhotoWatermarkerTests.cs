using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Evidence;
using ImageMagick;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Evidence;

public class FieldPhotoWatermarkerTests
{
    private static readonly FieldPhotoEvidenceMetadata Metadata = new(
        InspectorId: "INSP-001",
        PoleId: "POLE-123",
        UtcTimestamp: new DateTimeOffset(2026, 3, 1, 14, 30, 0, TimeSpan.Zero),
        Latitude: -22.906847,
        Longitude: -43.172897,
        CompassHeadingDegrees: 275.5,
        OrderId: "ORDER-999");

    private static byte[] CreateSyntheticPhotoBytes()
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, 64, 64);
        return image.ToByteArray(MagickFormat.Png);
    }

    [Fact]
    public void BurnWatermark_ReturnsAValidDecodableImage()
    {
        var watermarked = FieldPhotoWatermarker.BurnWatermark(CreateSyntheticPhotoBytes(), Metadata);

        using var image = new MagickImage(watermarked);
        image.Width.Should().Be(64);
        image.Height.Should().Be(64);
    }

    [Fact]
    public void ReadExifFields_RoundTripsAllFiveFields()
    {
        var watermarked = FieldPhotoWatermarker.BurnWatermark(CreateSyntheticPhotoBytes(), Metadata);

        var fields = FieldPhotoWatermarker.ReadExifFields(watermarked);

        fields.InspectorId.Should().Be(Metadata.InspectorId);
        fields.PoleId.Should().Be(Metadata.PoleId);
        fields.UtcTimestamp.Should().Be(Metadata.UtcTimestamp);
        fields.Latitude.Should().BeApproximately(Metadata.Latitude, 0.0001);
        fields.Longitude.Should().BeApproximately(Metadata.Longitude, 0.0001);
        fields.CompassHeadingDegrees.Should().BeApproximately(Metadata.CompassHeadingDegrees, 0.1);
    }

    [Fact]
    public void ReadExifFields_PositiveHemisphereCoordinates_RoundTripWithCorrectSign()
    {
        var northEastMetadata = Metadata with { Latitude = 40.712776, Longitude = 74.005974 };
        var watermarked = FieldPhotoWatermarker.BurnWatermark(CreateSyntheticPhotoBytes(), northEastMetadata);

        var fields = FieldPhotoWatermarker.ReadExifFields(watermarked);

        fields.Latitude.Should().BeApproximately(40.712776, 0.0001);
        fields.Longitude.Should().BeApproximately(74.005974, 0.0001);
    }

    [Fact]
    public void BurnWatermark_PixelsVisiblyChangeFromOriginal()
    {
        var original = CreateSyntheticPhotoBytes();
        var watermarked = FieldPhotoWatermarker.BurnWatermark(original, Metadata);

        using var originalImage = new MagickImage(original);
        using var watermarkedImage = new MagickImage(watermarked);

        var distortion = originalImage.Compare(watermarkedImage, ErrorMetric.Fuzz);

        distortion.Should().BeGreaterThan(0, "the watermark must visibly burn pixels, not just add metadata");
    }
}
