using FluentAssertions;
using GeoAssets.Shared.Services.Evidence;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Evidence;

public class EvidenceWatermarkTextFormatterTests
{
    [Fact]
    public void BuildLines_ConvertsTimestampToUtcMinus3()
    {
        var context = new EvidenceCaptureContext(
            OrderId: "order-1",
            TechnicianId: "tech-7",
            AssetTag: "US-EL-014",
            CapturedAtUtc: new DateTimeOffset(2026, 11, 1, 14, 32, 0, TimeSpan.Zero),
            CompassHeadingDegrees: 180);
        var fix = new GpsFix(-23.55052, -46.633309, 3.2, IsFromMockProvider: false);

        var lines = EvidenceWatermarkTextFormatter.BuildLines(context, fix);

        lines.Should().HaveCount(3);
        lines[0].Should().Contain("order-1").And.Contain("US-EL-014");
        lines[1].Should().Contain("2026-11-01 11:32:00").And.Contain("UTC-3").And.Contain("tech-7");
        lines[2].Should().Contain("-23.550520").And.Contain("-46.633309").And.Contain("3.2");
    }
}
