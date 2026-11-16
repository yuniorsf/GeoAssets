using FluentAssertions;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>One or more tests per guard/branch id of <see cref="SelectionFenceValidator.Validate"/> (XD01-307).</summary>
public class SelectionFenceValidatorTests
{
    // Counter-clockwise unit square, closed.
    private static double[][] Square(double minLon = 0, double minLat = 0, double size = 1) =>
    [
        [minLon, minLat], [minLon + size, minLat], [minLon + size, minLat + size], [minLon, minLat + size], [minLon, minLat],
    ];

    private static void AssertDefect(double[][][] rings, SelectionFenceDefect expected, int maxVertices = SelectionFenceValidator.DefaultMaxVertices) =>
        FluentActions.Invoking(() => SelectionFenceValidator.Validate(rings, maxVertices))
            .Should().Throw<InvalidSelectionFenceException>()
            .Which.Defect.Should().Be(expected);

    [Fact]
    public void FEN_G1_NullRings_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => SelectionFenceValidator.Validate(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("rings");

    [Fact]
    public void FEN_G2_ZeroRings_ThrowsEmpty() =>
        AssertDefect([], SelectionFenceDefect.Empty);

    [Fact]
    public void FEN_G3_NullRing_ThrowsMalformedPosition() =>
        AssertDefect([Square(), null!], SelectionFenceDefect.MalformedPosition);

    [Fact]
    public void FEN_G3_NullPosition_ThrowsMalformedPosition() =>
        AssertDefect([[[0, 0], null!, [1, 1], [0, 0]]], SelectionFenceDefect.MalformedPosition);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void FEN_G3_PositionLengthOtherThanTwo_ThrowsMalformedPosition(int length)
    {
        var bad = Enumerable.Repeat(0.5, length).ToArray();
        AssertDefect([[[0, 0], [1, 0], bad, [0, 0]]], SelectionFenceDefect.MalformedPosition);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void FEN_G4_NonFiniteCoordinate_ThrowsNonFiniteCoordinate(double lon, double lat) =>
        AssertDefect([[[0, 0], [1, 0], [lon, lat], [0, 0]]], SelectionFenceDefect.NonFiniteCoordinate);

    [Theory]
    [InlineData(180.0001, 0)]
    [InlineData(-180.0001, 0)]
    [InlineData(0, 90.0001)]
    [InlineData(0, -90.0001)]
    public void FEN_G5_CoordinateOutsideLonLatRange_ThrowsOutOfRange(double lon, double lat) =>
        AssertDefect([[[0, 0], [1, 0], [lon, lat], [0, 0]]], SelectionFenceDefect.OutOfRange);

    [Fact]
    public void FEN_G5_CoordinatesExactlyOnTheRangeLimits_AreAccepted()
    {
        double[][][] rings = [[[-90, -90], [90, -90], [90, 90], [-90, 90], [-90, -90]]];

        SelectionFenceValidator.Validate(rings).Coordinates.Should().BeEquivalentTo(rings);
    }

    [Fact]
    public void FEN_G6_RingWithFewerThanFourPositions_ThrowsTooFewVertices() =>
        AssertDefect([[[0, 0], [1, 0], [0, 0]]], SelectionFenceDefect.TooFewVertices);

    [Fact]
    public void FEN_G6_EmptyRing_ThrowsTooFewVertices() =>
        AssertDefect([Square(), []], SelectionFenceDefect.TooFewVertices);

    [Fact]
    public void FEN_G7_FirstPositionDiffersFromLast_ThrowsNotClosed_NoAutoClose() =>
        AssertDefect([[[0, 0], [1, 0], [1, 1], [0, 1]]], SelectionFenceDefect.NotClosed);

    [Fact]
    public void FEN_G7_UnclosedHole_ThrowsNotClosed() =>
        AssertDefect(
            [Square(size: 10), [[1, 1], [2, 1], [2, 2], [1, 2]]],
            SelectionFenceDefect.NotClosed);

    [Fact]
    public void FEN_G8_TotalVerticesAboveMaxVertices_ThrowsTooManyVertices() =>
        AssertDefect([Square()], SelectionFenceDefect.TooManyVertices, maxVertices: 4);

    [Fact]
    public void FEN_G8_VerticesAreCountedOverAllRings() =>
        AssertDefect(
            [Square(size: 10), Square(1, 1), Square(4, 4)],
            SelectionFenceDefect.TooManyVertices,
            maxVertices: 14);

    [Fact]
    public void FEN_G8_TotalVerticesEqualToMaxVertices_IsAccepted() =>
        SelectionFenceValidator.Validate([Square()], maxVertices: 5).Should().NotBeNull();

    [Fact]
    public void FEN_G8_DefaultCeilingIsTwoThousandVertices()
    {
        SelectionFenceValidator.DefaultMaxVertices.Should().Be(2000);
        AssertDefect([Circle(2001)], SelectionFenceDefect.TooManyVertices);
        SelectionFenceValidator.Validate([Circle(2000)]).Should().NotBeNull();
    }

    [Fact]
    public void FEN_G9_ExteriorLongitudeSpanAbove180_ThrowsCrossesAntimeridian() =>
        AssertDefect(
            [[[-90.5, 0], [90, 0], [90, 1], [-90.5, 1], [-90.5, 0]]],
            SelectionFenceDefect.CrossesAntimeridian);

    [Fact]
    public void FEN_G9_ExteriorLongitudeSpanOfExactly180_IsAccepted() =>
        SelectionFenceValidator.Validate([[[-90, 0], [90, 0], [90, 1], [-90, 1], [-90, 0]]]).Should().NotBeNull();

    [Fact]
    public void FEN_G10_SelfIntersectingBowTie_ThrowsSelfIntersecting_NoAutoRepair() =>
        AssertDefect([[[0, 0], [1, 1], [1, 0], [0, 1], [0, 0]]], SelectionFenceDefect.SelfIntersecting);

    [Fact]
    public void FEN_G10_HoleOutsideShell_ThrowsSelfIntersecting() =>
        AssertDefect([Square(), Square(5, 5)], SelectionFenceDefect.SelfIntersecting);

    [Fact]
    public void FEN_G11_ValidRingEnclosingNoArea_ThrowsZeroArea() =>
        // A topologically valid square so small that its area underflows to zero.
        AssertDefect([Square(size: 1e-170)], SelectionFenceDefect.ZeroArea);

    [Fact]
    public void FEN_B1_CounterClockwiseRing_IsAcceptedWithCoordinatesUnchanged()
    {
        double[][][] rings = [[[0, 0], [2, 0], [2, 2], [0, 2], [0, 0]]];

        var polygon = SelectionFenceValidator.Validate(rings);

        polygon.Coordinates.Should().BeEquivalentTo(rings, o => o.WithStrictOrdering());
    }

    [Fact]
    public void FEN_B1_ClockwiseRing_IsAcceptedWithCoordinatesUnchanged()
    {
        double[][][] rings = [[[0, 0], [0, 2], [2, 2], [2, 0], [0, 0]]];

        var polygon = SelectionFenceValidator.Validate(rings);

        polygon.Coordinates.Should().BeEquivalentTo(rings, o => o.WithStrictOrdering());
    }

    [Fact]
    public void FEN_B1_ShellWithHole_IsAcceptedWithCoordinatesUnchanged()
    {
        double[][][] rings = [Square(size: 10), Square(2, 2)];

        var polygon = SelectionFenceValidator.Validate(rings);

        polygon.Coordinates.Should().BeEquivalentTo(rings, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Exception_CarriesTheDefectInItsMessage()
    {
        var ex = new InvalidSelectionFenceException(SelectionFenceDefect.NotClosed);

        ex.Defect.Should().Be(SelectionFenceDefect.NotClosed);
        ex.Message.Should().Contain(nameof(SelectionFenceDefect.NotClosed));
    }

    /// <summary>A closed regular polygon with exactly <paramref name="positions"/> positions (closing one included).</summary>
    private static double[][] Circle(int positions)
    {
        var ring = new double[positions][];
        for (var i = 0; i < positions - 1; i++)
        {
            var angle = 2 * Math.PI * i / (positions - 1);
            ring[i] = [Math.Cos(angle), Math.Sin(angle)];
        }

        ring[^1] = [ring[0][0], ring[0][1]];
        return ring;
    }
}
