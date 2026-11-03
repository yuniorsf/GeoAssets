using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Evidence;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Isp.NeutralOperator;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

public class ClandestineFiberNoticeDossierGeneratorTests
{
    private static GeoFeature PoleFeature(string codId = "POLE-123", double lon = -43.2, double lat = -22.9) => new()
    {
        Id = "pole-1",
        Geometry = new GeoPoint(lon, lat),
        Properties = new GeoFeatureProperties { CustomAttributes = { ["COD_ID"] = codId } },
    };

    private static FieldPhotoEvidenceMetadata Metadata() => new(
        InspectorId: "inspector-1",
        PoleId: "pole-1",
        UtcTimestamp: new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
        Latitude: -22.9,
        Longitude: -43.2,
        CompassHeadingDegrees: 90,
        OrderId: "order-1");

    private static readonly DateTimeOffset FixedNow = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_IncludesPoleCodeCoordinatesEvidenceDigestAndStatutoryText()
    {
        var pole = PoleFeature();
        var flaggedAt = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var deadline = flaggedAt.AddDays(30);

        var result = ClandestineFiberNoticeDossierGenerator.Generate(
            pole, Metadata(), "digest-abc==", flaggedAt, deadline);

        result.DocumentText.Should().Contain("POLE-123");
        result.DocumentText.Should().Contain("digest-abc==");
        result.DocumentText.Should().Contain("NOTIFICAÇÃO DE IRREGULARIDADE");
        result.DocumentText.Should().Contain("30 (trinta) dias");
        result.DeadlineUtc.Should().Be(deadline);
    }

    [Fact]
    public void Generate_WithSealedPhotoBytes_AttachesThem()
    {
        var pole = PoleFeature();
        byte[] jpegBytes = [0x01, 0x02, 0x03];

        var result = ClandestineFiberNoticeDossierGenerator.Generate(
            pole, Metadata(), "digest-abc==", FixedNow, FixedNow.AddDays(30), jpegBytes);

        result.AttachmentFiles.Should().ContainSingle(f => f.Content == jpegBytes);
    }

    [Fact]
    public void Generate_WithoutSealedPhotoBytes_HasNoAttachments()
    {
        var pole = PoleFeature();

        var result = ClandestineFiberNoticeDossierGenerator.Generate(
            pole, Metadata(), "digest-abc==", FixedNow, FixedNow.AddDays(30));

        result.AttachmentFiles.Should().BeEmpty();
    }

    [Fact]
    public void Generate_PoleMissingCodId_Throws()
    {
        var pole = new GeoFeature { Id = "pole-1", Geometry = new GeoPoint(-43.2, -22.9) };

        var act = () => ClandestineFiberNoticeDossierGenerator.Generate(
            pole, Metadata(), "digest-abc==", FixedNow, FixedNow.AddDays(30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_PoleWithoutPointGeometry_Throws()
    {
        var pole = new GeoFeature
        {
            Id = "pole-1",
            Properties = new GeoFeatureProperties { CustomAttributes = { ["COD_ID"] = "POLE-123" } },
        };

        var act = () => ClandestineFiberNoticeDossierGenerator.Generate(
            pole, Metadata(), "digest-abc==", FixedNow, FixedNow.AddDays(30));

        act.Should().Throw<ArgumentException>();
    }
}
