using FluentAssertions;
using GeoAssets.Core.Isp;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Isp;

public class CtoOccupancyHeatmapQueryTests
{
    private static GeoFeature CtoFeature(string id, int occupied, int total, GeoPoint? geometry = null) =>
        new()
        {
            Id = id,
            Geometry = geometry ?? new GeoPoint(-46.633309, -23.55052),
            Properties = new GeoFeatureProperties
            {
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CtoCapacityCalculator.OccupiedSplitterPortsKey] = occupied.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [CtoCapacityCalculator.TotalSplitterPortsKey] = total.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
            },
        };

    [Fact]
    public void GetAboveThreshold_FeatureAboveThreshold_IsIncluded()
    {
        var feature = CtoFeature("cto-1", occupied: 9, total: 10);

        var result = CtoOccupancyHeatmapQuery.GetAboveThreshold([feature], thresholdPercentage: 80);

        result.Should().ContainSingle();
        result[0].CtoFeatureId.Should().Be("cto-1");
        result[0].OccupiedPorts.Should().Be(9);
        result[0].TotalPorts.Should().Be(10);
        result[0].OccupiedPercentage.Should().Be(90);
    }

    [Fact]
    public void GetAboveThreshold_FeatureExactlyAtThreshold_IsIncluded()
    {
        var feature = CtoFeature("cto-1", occupied: 8, total: 10);

        var result = CtoOccupancyHeatmapQuery.GetAboveThreshold([feature], thresholdPercentage: 80);

        result.Should().ContainSingle();
        result[0].CtoFeatureId.Should().Be("cto-1");
    }

    [Fact]
    public void GetAboveThreshold_FeatureBelowThreshold_IsExcluded()
    {
        var feature = CtoFeature("cto-1", occupied: 7, total: 10);

        var result = CtoOccupancyHeatmapQuery.GetAboveThreshold([feature], thresholdPercentage: 80);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAboveThreshold_FeatureWithNullGeometry_IsSkippedEvenIfAboveThreshold()
    {
        var feature = new GeoFeature
        {
            Id = "cto-1",
            Geometry = null,
            Properties = new GeoFeatureProperties
            {
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "10",
                    [CtoCapacityCalculator.TotalSplitterPortsKey] = "10",
                },
            },
        };

        var result = CtoOccupancyHeatmapQuery.GetAboveThreshold([feature], thresholdPercentage: 50);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAboveThreshold_EmptyInput_ReturnsEmptyList()
    {
        var result = CtoOccupancyHeatmapQuery.GetAboveThreshold([], thresholdPercentage: 50);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAboveThreshold_NullInput_ThrowsArgumentNullException()
    {
        var act = () => CtoOccupancyHeatmapQuery.GetAboveThreshold(null!, thresholdPercentage: 50);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetAboveThreshold_MultipleFeatures_FiltersEachIndependently()
    {
        var aboveThreshold = CtoFeature("cto-above", occupied: 9, total: 10);
        var belowThreshold = CtoFeature("cto-below", occupied: 1, total: 10);
        var noGeometry = new GeoFeature
        {
            Id = "cto-no-geometry",
            Geometry = null,
            Properties = new GeoFeatureProperties
            {
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CtoCapacityCalculator.OccupiedSplitterPortsKey] = "10",
                    [CtoCapacityCalculator.TotalSplitterPortsKey] = "10",
                },
            },
        };

        var result = CtoOccupancyHeatmapQuery.GetAboveThreshold(
            [aboveThreshold, belowThreshold, noGeometry], thresholdPercentage: 80);

        result.Should().ContainSingle();
        result[0].CtoFeatureId.Should().Be("cto-above");
    }
}
