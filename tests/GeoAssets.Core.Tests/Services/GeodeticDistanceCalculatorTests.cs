using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class GeodeticDistanceCalculatorTests
{
    // Must match GeodeticDistanceCalculator's own constant — used here only to construct a point
    // at an exact, known distance from a reference point via the simple due-north case of the
    // direct geodesic problem (moving due north by d meters changes latitude by exactly d/R
    // radians and leaves longitude unchanged), so the boundary tests below are self-consistent
    // with the formula under test rather than cross-checked against an independent source.
    private const double EarthRadiusMeters = 6_371_008.8;

    private static GeoPoint MoveNorthByMeters(GeoPoint origin, double meters)
    {
        var deltaLatRadians = meters / EarthRadiusMeters;
        var newLatRadians = origin.Latitude * Math.PI / 180.0 + deltaLatRadians;
        return new GeoPoint(origin.Longitude, newLatRadians * 180.0 / Math.PI);
    }

    private static readonly GeoPoint Origin = new(-46.633, -23.55);

    [Fact]
    public void DistanceInMeters_SamePoint_IsZero() =>
        GeodeticDistanceCalculator.DistanceInMeters(Origin, Origin).Should().BeApproximately(0, 0.001);

    [Fact]
    public void DistanceInMeters_PointMovedNorthByKnownDistance_MatchesThatDistance()
    {
        var moved = MoveNorthByMeters(Origin, 1000);

        GeodeticDistanceCalculator.DistanceInMeters(Origin, moved).Should().BeApproximately(1000, 0.01);
    }

    [Fact]
    public void IsWithinDistance_ExactlyAtTolerance_ReturnsTrue()
    {
        // Tests the boundary against the distance this same formula actually computes for the
        // constructed point, not a hardcoded "30" — MoveNorthByMeters round-trips through
        // radians/degrees, which can leave the computed distance a few micrometers off 30 (still
        // well within a GPS fix's real-world accuracy), and a hardcoded 30 would make this test
        // flaky over nothing but that float rounding rather than real boundary behavior.
        var atThirtyMeters = MoveNorthByMeters(Origin, 30);
        var distance = GeodeticDistanceCalculator.DistanceInMeters(Origin, atThirtyMeters);
        distance.Should().BeApproximately(30, 0.001);

        GeodeticDistanceCalculator.IsWithinDistance(Origin, atThirtyMeters, toleranceMeters: distance).Should().BeTrue(
            "the boundary itself must pass, matching this codebase's other boundary-inclusive spatial predicates");
    }

    [Fact]
    public void IsWithinDistance_JustOverTolerance_ReturnsFalse()
    {
        var atThirtyOneMeters = MoveNorthByMeters(Origin, 31);

        GeodeticDistanceCalculator.IsWithinDistance(Origin, atThirtyOneMeters, toleranceMeters: 30).Should().BeFalse();
    }

    [Fact]
    public void IsWithinDistance_WellWithinTolerance_ReturnsTrue()
    {
        var atFiveMeters = MoveNorthByMeters(Origin, 5);

        GeodeticDistanceCalculator.IsWithinDistance(Origin, atFiveMeters, toleranceMeters: 30).Should().BeTrue();
    }

    [Fact]
    public void DistanceInMeters_NullArgument_Throws()
    {
        var act = () => GeodeticDistanceCalculator.DistanceInMeters(null!, Origin);

        act.Should().Throw<ArgumentNullException>();
    }
}
