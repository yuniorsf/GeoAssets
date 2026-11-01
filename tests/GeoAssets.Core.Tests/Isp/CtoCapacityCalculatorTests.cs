using FluentAssertions;
using GeoAssets.Core.Isp;
using Xunit;

namespace GeoAssets.Core.Tests.Isp;

public class CtoCapacityCalculatorTests
{
    [Fact]
    public void OccupiedPortPercentage_PartiallyOccupied_ReturnsPercentage()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CtoCapacityCalculator.TotalSplitterPortsKey] = "16",
            [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "12",
        };

        CtoCapacityCalculator.OccupiedPortPercentage(attributes).Should().Be(75);
    }

    [Fact]
    public void OccupiedPortPercentage_FullyOccupied_ReturnsOneHundred()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CtoCapacityCalculator.TotalSplitterPortsKey] = "8",
            [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "8",
        };

        CtoCapacityCalculator.OccupiedPortPercentage(attributes).Should().Be(100);
    }

    [Fact]
    public void OccupiedPortPercentage_ZeroTotal_ReturnsZero()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CtoCapacityCalculator.TotalSplitterPortsKey] = "0",
            [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "0",
        };

        CtoCapacityCalculator.OccupiedPortPercentage(attributes).Should().Be(0);
    }

    [Fact]
    public void OccupiedPortPercentage_MissingKeys_ReturnsZero() =>
        CtoCapacityCalculator.OccupiedPortPercentage(new Dictionary<string, string>(StringComparer.Ordinal)).Should().Be(0);

    [Fact]
    public void OccupiedPortPercentage_NonNumericValues_ReturnsZero()
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CtoCapacityCalculator.TotalSplitterPortsKey] = "not-a-number",
            [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "12",
        };

        CtoCapacityCalculator.OccupiedPortPercentage(attributes).Should().Be(0);
    }
}
