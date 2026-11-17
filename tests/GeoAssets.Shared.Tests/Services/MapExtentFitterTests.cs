using FluentAssertions;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>MAP-B24 (XD01-311): what <see cref="MapExtentFitter"/> hands to <c>FitBoundsAsync</c>.</summary>
public class MapExtentFitterTests
{
    private const double Tolerance = 1e-9;
    private const double MinSpan = MapExtentFitter.MinSpanDegrees;

    private static void ShouldBe(double[]? actual, double minLon, double minLat, double maxLon, double maxLat)
    {
        actual.Should().NotBeNull().And.HaveCount(4);
        actual![0].Should().BeApproximately(minLon, Tolerance);
        actual[1].Should().BeApproximately(minLat, Tolerance);
        actual[2].Should().BeApproximately(maxLon, Tolerance);
        actual[3].Should().BeApproximately(maxLat, Tolerance);
    }

    [Fact]
    public void MAP_B24_NullExtent_YieldsNothingToFit() =>
        MapExtentFitter.ToFitBounds(null).Should().BeNull();

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, double.NaN, 1, 1)]
    [InlineData(0, 0, double.PositiveInfinity, 1)]
    [InlineData(0, 0, 1, double.NegativeInfinity)]
    public void MAP_B24_NonFiniteCoordinate_YieldsNothingToFit(double minLon, double minLat, double maxLon, double maxLat) =>
        MapExtentFitter.ToFitBounds(new OrderTargetExtent(minLon, minLat, maxLon, maxLat)).Should().BeNull();

    [Fact]
    public void MAP_B24_NormalExtent_PassesThroughInLonLatOrder()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(-46.70, -23.60, -46.60, -23.50));

        ShouldBe(bbox, -46.70, -23.60, -46.60, -23.50);
    }

    [Fact]
    public void MAP_B24_ExtentExactlyAtTheMinimumSpan_IsNotPadded()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(10, 20, 10 + MinSpan, 20 + MinSpan));

        ShouldBe(bbox, 10, 20, 10 + MinSpan, 20 + MinSpan);
    }

    [Fact]
    public void MAP_B24_SinglePoint_IsPaddedToTheMinimumSpanAroundThePoint()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(-46.633, -23.55, -46.633, -23.55));

        ShouldBe(bbox, -46.633 - MinSpan / 2, -23.55 - MinSpan / 2, -46.633 + MinSpan / 2, -23.55 + MinSpan / 2);
    }

    [Fact]
    public void MAP_B24_ZeroWidthExtent_PadsOnlyTheLongitude()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(-46.633, -23.60, -46.633, -23.50));

        ShouldBe(bbox, -46.633 - MinSpan / 2, -23.60, -46.633 + MinSpan / 2, -23.50);
    }

    [Fact]
    public void MAP_B24_ZeroHeightExtent_PadsOnlyTheLatitude()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(-46.70, -23.55, -46.60, -23.55));

        ShouldBe(bbox, -46.70, -23.55 - MinSpan / 2, -46.60, -23.55 + MinSpan / 2);
    }

    [Fact]
    public void MAP_B24_NearlyDegenerateExtent_IsGrownToTheMinimumSpanAroundItsMiddle()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(10.000, 20.000, 10.002, 20.004));

        ShouldBe(bbox, 10.001 - MinSpan / 2, 20.002 - MinSpan / 2, 10.001 + MinSpan / 2, 20.002 + MinSpan / 2);
    }

    [Fact]
    public void MAP_B24_CornersGivenTheWrongWayRound_ArePutInOrder()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(-46.60, -23.50, -46.70, -23.60));

        ShouldBe(bbox, -46.70, -23.60, -46.60, -23.50);
    }

    [Fact]
    public void MAP_B24_TargetsOnBothSidesOfTheAntimeridian_FitTheWholeSpanWithoutWrapping()
    {
        // MIN/MAX over longitudes 179.9 and -179.9 is the long way round; it must stay a plain,
        // ordered box inside [-180, 180] rather than a wrapped one the map cannot query.
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(-179.9, -17.0, 179.9, -16.0));

        ShouldBe(bbox, -179.9, -17.0, 179.9, -16.0);
    }

    [Theory]
    [InlineData(180.0, 0.0)]
    [InlineData(-180.0, 0.0)]
    [InlineData(179.999, 89.999)]
    [InlineData(-179.999, -89.999)]
    [InlineData(0.0, 90.0)]
    [InlineData(0.0, -90.0)]
    public void MAP_B24_PointAtTheEdgeOfTheWorld_IsPaddedInwards_NeverPastTheAntimeridianOrAPole(double lon, double lat)
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(lon, lat, lon, lat))!;

        bbox[0].Should().BeGreaterThanOrEqualTo(-180).And.BeLessThanOrEqualTo(lon);
        bbox[2].Should().BeLessThanOrEqualTo(180).And.BeGreaterThanOrEqualTo(lon);
        bbox[1].Should().BeGreaterThanOrEqualTo(-90).And.BeLessThanOrEqualTo(lat);
        bbox[3].Should().BeLessThanOrEqualTo(90).And.BeGreaterThanOrEqualTo(lat);
        (bbox[2] - bbox[0]).Should().BeApproximately(MinSpan, Tolerance);
        (bbox[3] - bbox[1]).Should().BeApproximately(MinSpan, Tolerance);
    }

    [Fact]
    public void MAP_B24_OutOfRangeCoordinates_AreClampedToTheWorld()
    {
        var bbox = MapExtentFitter.ToFitBounds(new OrderTargetExtent(-200, -100, 200, 100));

        ShouldBe(bbox, -180, -90, 180, 90);
    }
}
