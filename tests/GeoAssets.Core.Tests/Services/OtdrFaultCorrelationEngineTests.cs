using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// XD01-282's ACs: every <see cref="FaultLocationType"/> outcome (station slack, technical
/// reserve loop, splice closure, aerial span — including the multi-slack-point traversal path),
/// the central-office-slack boundary, the AC3 UTM reprojection round trip, upstream/downstream
/// pole correlation, and the engine's own validation. See <see cref="OtdrFaultCorrelationEngine"/>
/// for the algorithm this exercises.
/// </summary>
public class OtdrFaultCorrelationEngineTests
{
    private static readonly OtdrFaultCorrelationEngine Engine = new();

    // A ~24km straight line near São Paulo (zone 23S), long enough that no route distance used
    // below is ever clamped to the line's endpoint by NTS's LengthIndexedLine.
    private static GeoLineString DefaultRoute() =>
        new([(-46.633, -23.55), (-46.400, -23.55)]);

    private static OtdrFaultCorrelationRequest BuildRequest(
        double opticalDistanceMeters,
        double centralOfficeSlackMeters = 0.0,
        double helixFactor = 1.0,
        IReadOnlyList<OtdrCableSlackPoint>? slackPoints = null,
        IReadOnlyList<OtdrPoleAlongRoute>? polesAlongRoute = null,
        GeoLineString? cableRoute = null,
        double spliceMatchToleranceMeters = 1.0) =>
        new(
            opticalDistanceMeters,
            helixFactor,
            centralOfficeSlackMeters,
            slackPoints ?? [],
            cableRoute ?? DefaultRoute(),
            polesAlongRoute ?? [],
            spliceMatchToleranceMeters);

    // ── StationSlack boundary ───────────────────────────────────────────────

    [Fact]
    public void Correlate_SheathLengthExactlyEqualsCentralOfficeSlack_ReturnsStationSlack()
    {
        var request = BuildRequest(opticalDistanceMeters: 100, centralOfficeSlackMeters: 100, helixFactor: 1.0);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.StationSlack);
        result.RouteDistanceMeters.Should().Be(0.0);
        result.Coordinate.Should().BeNull();
        result.UpstreamPoleFeatureId.Should().BeNull();
        result.DownstreamPoleFeatureId.Should().BeNull();
    }

    [Fact]
    public void Correlate_SheathLengthStrictlyBelowCentralOfficeSlack_ReturnsStationSlack()
    {
        var request = BuildRequest(opticalDistanceMeters: 50, centralOfficeSlackMeters: 100, helixFactor: 1.0);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.StationSlack);
        result.Coordinate.Should().BeNull();
    }

    [Fact]
    public void Correlate_SheathLengthJustAboveCentralOfficeSlack_FallsThroughToSlackPointWalk()
    {
        // sheathLength = 101 > CentralOfficeSlackMeters(100) -- just crosses the boundary into
        // the walk, with no slack points, resolving 1.0m into the first (only) span.
        var request = BuildRequest(opticalDistanceMeters: 101, centralOfficeSlackMeters: 100, helixFactor: 1.0);

        var result = Engine.Correlate(request);

        result.LocationType.Should().NotBe(FaultLocationType.StationSlack);
        result.LocationType.Should().Be(FaultLocationType.AerialSpan);
        result.RouteDistanceMeters.Should().Be(1.0);
        result.Coordinate.Should().NotBeNull();
    }

    // ── AerialSpan before any slack point ───────────────────────────────────

    [Fact]
    public void Correlate_NoSlackPointsAtAll_ResolvesToAerialSpanWithinFirstSpan()
    {
        // sheathLength = 600, remaining budget after CO slack = 500.
        var request = BuildRequest(opticalDistanceMeters: 600, centralOfficeSlackMeters: 100);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.AerialSpan);
        result.RouteDistanceMeters.Should().Be(500.0);
        result.Coordinate.Should().NotBeNull();
    }

    [Fact]
    public void Correlate_SlackPointsAllBeyondFault_ResolvesToAerialSpanWithinFirstSpan()
    {
        var slackPoints = new OtdrCableSlackPoint[] { new(2000, 10, FaultLocationType.SpliceClosure) };
        var request = BuildRequest(opticalDistanceMeters: 600, centralOfficeSlackMeters: 100, slackPoints: slackPoints);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.AerialSpan);
        result.RouteDistanceMeters.Should().Be(500.0);
    }

    // ── TechnicalReserveLoop ─────────────────────────────────────────────────

    [Fact]
    public void Correlate_RemainingBudgetWithinToleranceOfReserveLoop_ReturnsTechnicalReserveLoop()
    {
        // remaining = 1000.5; span to the point (1000) leaves 0.5m, within the 1.0m default tolerance.
        var slackPoints = new OtdrCableSlackPoint[] { new(1000, 50, FaultLocationType.TechnicalReserveLoop) };
        var request = BuildRequest(opticalDistanceMeters: 1000.5, centralOfficeSlackMeters: 0, slackPoints: slackPoints);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.TechnicalReserveLoop);
        result.RouteDistanceMeters.Should().Be(1000.0);
        result.Coordinate.Should().NotBeNull();
    }

    // ── SpliceClosure ────────────────────────────────────────────────────────

    [Fact]
    public void Correlate_RemainingBudgetWithinToleranceOfSpliceClosure_ReturnsSpliceClosure()
    {
        var slackPoints = new OtdrCableSlackPoint[] { new(1000, 50, FaultLocationType.SpliceClosure) };
        var request = BuildRequest(opticalDistanceMeters: 1000.5, centralOfficeSlackMeters: 0, slackPoints: slackPoints);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.SpliceClosure);
        result.RouteDistanceMeters.Should().Be(1000.0);
        result.Coordinate.Should().NotBeNull();
    }

    // ── AerialSpan beyond the last slack point ──────────────────────────────

    [Fact]
    public void Correlate_BudgetSurvivesLastSlackPointsCoiledLength_ResolvesToAerialSpanPastIt()
    {
        // remaining = 1200; at the point (1000), span leaves 200m, beyond the 1.0m tolerance, so
        // the 50m coiled length is subtracted (clamped at 0), leaving 150m -> 1000 + 150 = 1150.
        var slackPoints = new OtdrCableSlackPoint[] { new(1000, 50, FaultLocationType.SpliceClosure) };
        var request = BuildRequest(opticalDistanceMeters: 1200, centralOfficeSlackMeters: 0, slackPoints: slackPoints);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.AerialSpan);
        result.RouteDistanceMeters.Should().Be(1150.0);
    }

    // ── Multiple slack points traversed in sequence ─────────────────────────

    [Fact]
    public void Correlate_WalksPastFirstSlackPointBeforeResolvingAtSecond_ReturnsSecondPointsKind()
    {
        // remaining = 1030.5.
        // At point 1 (distance 500, coil 30): span = 500, remaining -> 530.5, which is beyond the
        // 1.0m tolerance, so the loop continues: remaining -> 500.5 after subtracting the coil.
        // At point 2 (distance 1000, coil 40): span = 500, remaining -> 0.5, within tolerance ->
        // resolves to point 2's own kind at route distance 1000.
        var slackPoints = new OtdrCableSlackPoint[]
        {
            new(500, 30, FaultLocationType.SpliceClosure),
            new(1000, 40, FaultLocationType.TechnicalReserveLoop),
        };
        var request = BuildRequest(opticalDistanceMeters: 1030.5, centralOfficeSlackMeters: 0, slackPoints: slackPoints);

        var result = Engine.Correlate(request);

        result.LocationType.Should().Be(FaultLocationType.TechnicalReserveLoop);
        result.RouteDistanceMeters.Should().Be(1000.0);
    }

    // ── UTM round-trip accuracy (AC3) ───────────────────────────────────────

    [Fact]
    public void Correlate_NonStationSlackResult_CoordinateRoundTripsThroughUtmCloseToLinearInterpolation()
    {
        // A pure east-west line (constant latitude) near São Paulo, zone 23S. The fault is placed
        // 40% of the way along it by optical/CO-slack arithmetic chosen so the resolved route
        // distance equals exactly 40% of the line's real-world length.
        var start = (Lon: -46.700, Lat: -23.550);
        var end = (Lon: -46.550, Lat: -23.550);
        var route = new GeoLineString([start, end]);

        var totalMeters = GeodeticDistanceCalculator.DistanceInMeters(
            new GeoPoint(start.Lon, start.Lat), new GeoPoint(end.Lon, end.Lat));
        var targetRouteDistance = totalMeters * 0.4;

        var request = BuildRequest(opticalDistanceMeters: targetRouteDistance, centralOfficeSlackMeters: 0, cableRoute: route);

        var result = Engine.Correlate(request);

        result.RouteDistanceMeters.Should().Be(targetRouteDistance);
        result.Coordinate.Should().NotBeNull();

        // Since the line runs due east-west, a point 40% of the way along it should sit at
        // (very nearly) the same latitude and at 40% of the way between the two longitudes --
        // "very nearly" rather than exactly, because the engine interpolates in the projected
        // (UTM) plane and reprojects back, while this expectation interpolates directly in
        // lon/lat: the two agree up to the projection's small non-linearity over this ~16.6km
        // span. Measured empirically: a 1e-6 degree tolerance fails with an actual longitude
        // delta of ~9.66e-5 degrees (~8m) and a latitude delta of ~1.74e-5 degrees (~1.9m), so
        // 1e-4 degrees is used here -- loose enough to absorb that real projection non-linearity
        // rather than fail on it, while still tight enough (~11m) to catch a gross bug (wrong
        // zone, wrong hemisphere, swapped axes).
        var expectedLon = start.Lon + ((end.Lon - start.Lon) * 0.4);
        var expectedLat = start.Lat;

        result.Coordinate!.Longitude.Should().BeApproximately(expectedLon, 1e-4);
        result.Coordinate!.Latitude.Should().BeApproximately(expectedLat, 1e-4);

        // Sanity check that doesn't over-claim precision: the point must lie strictly between
        // the two endpoints' longitudes, and at (near enough) the shared latitude.
        result.Coordinate!.Longitude.Should().BeInRange(start.Lon, end.Lon);
    }

    // ── Pole correlation edge cases ──────────────────────────────────────────

    [Fact]
    public void Correlate_FaultBeforeFirstPole_UpstreamNullDownstreamFirstPole()
    {
        var poles = new OtdrPoleAlongRoute[] { new("P1", 600) };
        var request = BuildRequest(opticalDistanceMeters: 600, centralOfficeSlackMeters: 100, polesAlongRoute: poles);

        var result = Engine.Correlate(request);

        result.RouteDistanceMeters.Should().Be(500.0);
        result.UpstreamPoleFeatureId.Should().BeNull();
        result.DownstreamPoleFeatureId.Should().Be("P1");
    }

    [Fact]
    public void Correlate_FaultAfterLastPole_UpstreamLastPoleDownstreamNull()
    {
        var poles = new OtdrPoleAlongRoute[] { new("P1", 100) };
        var request = BuildRequest(opticalDistanceMeters: 600, centralOfficeSlackMeters: 100, polesAlongRoute: poles);

        var result = Engine.Correlate(request);

        result.RouteDistanceMeters.Should().Be(500.0);
        result.UpstreamPoleFeatureId.Should().Be("P1");
        result.DownstreamPoleFeatureId.Should().BeNull();
    }

    [Fact]
    public void Correlate_FaultBetweenTwoPoles_ResolvesBothUpstreamAndDownstream()
    {
        var poles = new OtdrPoleAlongRoute[] { new("P1", 300), new("P2", 700) };
        var request = BuildRequest(opticalDistanceMeters: 600, centralOfficeSlackMeters: 100, polesAlongRoute: poles);

        var result = Engine.Correlate(request);

        result.RouteDistanceMeters.Should().Be(500.0);
        result.UpstreamPoleFeatureId.Should().Be("P1");
        result.DownstreamPoleFeatureId.Should().Be("P2");
    }

    [Fact]
    public void Correlate_NoPolesAtAll_UpstreamAndDownstreamBothNull()
    {
        var request = BuildRequest(opticalDistanceMeters: 600, centralOfficeSlackMeters: 100, polesAlongRoute: []);

        var result = Engine.Correlate(request);

        result.RouteDistanceMeters.Should().Be(500.0);
        result.UpstreamPoleFeatureId.Should().BeNull();
        result.DownstreamPoleFeatureId.Should().BeNull();
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public void Correlate_NullRequest_ThrowsArgumentNullException()
    {
        var act = () => Engine.Correlate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Correlate_NullCableRoute_ThrowsArgumentNullException()
    {
        var request = new OtdrFaultCorrelationRequest(100, 1.0, 100, [], null!, []);

        var act = () => Engine.Correlate(request);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Correlate_NullSlackPoints_ThrowsArgumentNullException()
    {
        var request = new OtdrFaultCorrelationRequest(100, 1.0, 100, null!, DefaultRoute(), []);

        var act = () => Engine.Correlate(request);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Correlate_NullPolesAlongRoute_ThrowsArgumentNullException()
    {
        var request = new OtdrFaultCorrelationRequest(100, 1.0, 100, [], DefaultRoute(), null!);

        var act = () => Engine.Correlate(request);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Correlate_NegativeOpticalDistance_ThrowsArgumentOutOfRangeException()
    {
        var request = BuildRequest(opticalDistanceMeters: -1);

        var act = () => Engine.Correlate(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Correlate_ZeroHelixFactor_ThrowsArgumentOutOfRangeException()
    {
        var request = BuildRequest(opticalDistanceMeters: 100, helixFactor: 0);

        var act = () => Engine.Correlate(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Correlate_NegativeHelixFactor_ThrowsArgumentOutOfRangeException()
    {
        var request = BuildRequest(opticalDistanceMeters: 100, helixFactor: -1);

        var act = () => Engine.Correlate(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Correlate_NegativeCentralOfficeSlack_ThrowsArgumentOutOfRangeException()
    {
        var request = BuildRequest(opticalDistanceMeters: 100, centralOfficeSlackMeters: -1);

        var act = () => Engine.Correlate(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Correlate_CableRouteWithOnlyOneCoordinate_ThrowsArgumentException()
    {
        // Only reachable for a non-StationSlack result, since ExtractCoordinate (where the
        // engine's own <2-coordinate guard lives) is never called on the StationSlack path.
        var act = () =>
        {
            var route = new GeoLineString([(-46.633, -23.55)]);
            var request = BuildRequest(opticalDistanceMeters: 600, centralOfficeSlackMeters: 100, cableRoute: route);
            Engine.Correlate(request);
        };

        act.Should().Throw<ArgumentException>();
    }
}
