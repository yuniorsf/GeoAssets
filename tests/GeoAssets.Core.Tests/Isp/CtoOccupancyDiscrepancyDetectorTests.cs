using FluentAssertions;
using GeoAssets.Core.Isp;
using Xunit;

namespace GeoAssets.Core.Tests.Isp;

public class CtoOccupancyDiscrepancyDetectorTests
{
    [Fact]
    public void Detect_EqualCounts_HasNoDiscrepancy()
    {
        var discrepancy = CtoOccupancyDiscrepancyDetector.Detect("cto-1", bssReportedOccupied: 5, geoAssetsTrackedOccupied: 5);

        discrepancy.Difference.Should().Be(0);
        discrepancy.HasDiscrepancy.Should().BeFalse();
    }

    [Fact]
    public void Detect_BssHigher_ReturnsPositiveDifference()
    {
        var discrepancy = CtoOccupancyDiscrepancyDetector.Detect("cto-1", bssReportedOccupied: 8, geoAssetsTrackedOccupied: 5);

        discrepancy.Difference.Should().Be(3);
        discrepancy.HasDiscrepancy.Should().BeTrue();
    }

    [Fact]
    public void Detect_BssLower_ReturnsNegativeDifference()
    {
        var discrepancy = CtoOccupancyDiscrepancyDetector.Detect("cto-1", bssReportedOccupied: 2, geoAssetsTrackedOccupied: 5);

        discrepancy.Difference.Should().Be(-3);
        discrepancy.HasDiscrepancy.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_NullOrWhitespaceCtoFeatureId_ThrowsArgumentException(string? ctoFeatureId)
    {
        var act = () => CtoOccupancyDiscrepancyDetector.Detect(ctoFeatureId!, bssReportedOccupied: 1, geoAssetsTrackedOccupied: 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Detect_NegativeBssReportedOccupied_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CtoOccupancyDiscrepancyDetector.Detect("cto-1", bssReportedOccupied: -1, geoAssetsTrackedOccupied: 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Detect_NegativeGeoAssetsTrackedOccupied_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CtoOccupancyDiscrepancyDetector.Detect("cto-1", bssReportedOccupied: 5, geoAssetsTrackedOccupied: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
