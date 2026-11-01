using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class GeodeticAreaCalculatorTests
{
    // Must match GeodeticAreaCalculator's own constant — used here only to construct a polygon
    // of a known, exact real-world size via the same equirectangular scale factor the
    // calculator itself uses, so this test is self-consistent with the formula under test.
    private const double MetersPerDegreeLatitude = 111_320.0;

    private static GeoPolygon SquareOfSide(double originLon, double originLat, double sideMeters)
    {
        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(originLat * Math.PI / 180.0);
        var deltaLon = sideMeters / metersPerDegreeLongitude;
        var deltaLat = sideMeters / MetersPerDegreeLatitude;

        return new GeoPolygon([
            (originLon, originLat),
            (originLon + deltaLon, originLat),
            (originLon + deltaLon, originLat + deltaLat),
            (originLon, originLat + deltaLat),
            (originLon, originLat),
        ]);
    }

    [Fact]
    public void AreaSquareMeters_TenMeterSquare_IsOneHundredSquareMeters()
    {
        var polygon = SquareOfSide(-46.633, -23.55, sideMeters: 10);

        GeodeticAreaCalculator.AreaSquareMeters(polygon).Should().BeApproximately(100, 0.01);
    }

    [Fact]
    public void AreaSquareMeters_TwoMeterSquare_IsFourSquareMeters()
    {
        // At a trench's actual scale, not just an arbitrary round number.
        var polygon = SquareOfSide(-46.633, -23.55, sideMeters: 2);

        GeodeticAreaCalculator.AreaSquareMeters(polygon).Should().BeApproximately(4, 0.001);
    }

    [Fact]
    public void AreaSquareMeters_ScaleIsConsistentAcrossDifferentLatitudes()
    {
        // The longitude-degree-to-meters scale factor depends on latitude (cos(lat)) — this
        // proves the calculator actually applies that correction rather than assuming 1:1.
        var nearEquator = SquareOfSide(-46.633, 1.0, sideMeters: 10);
        var farFromEquator = SquareOfSide(-46.633, -55.0, sideMeters: 10);

        GeodeticAreaCalculator.AreaSquareMeters(nearEquator).Should().BeApproximately(100, 0.1);
        GeodeticAreaCalculator.AreaSquareMeters(farFromEquator).Should().BeApproximately(100, 0.1);
    }

    [Fact]
    public void AreaSquareMeters_NullArgument_Throws()
    {
        var act = () => GeodeticAreaCalculator.AreaSquareMeters(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
