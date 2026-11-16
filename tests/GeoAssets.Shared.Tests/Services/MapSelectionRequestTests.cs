using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>Gesture → selection-strategy request mapping (XD01-310): box normalisation, ring
/// closing, longitude/latitude order and the client-side fence pre-check.</summary>
public class MapSelectionRequestTests
{
    private static MapSelectionGesture Box(params double[][] positions) => new(MapSelectionTool.Box, [positions], Additive: false);

    private static MapSelectionGesture Fence(params double[][] positions) => new(MapSelectionTool.Fence, [positions], Additive: false);

    private static readonly double[][] _expectedRectangle =
        [[-70.5, 18.25], [-69.5, 18.25], [-69.5, 19.75], [-70.5, 19.75], [-70.5, 18.25]];

    [Theory]
    [InlineData(-70.5, 18.25, -69.5, 19.75)] // south-west → north-east
    [InlineData(-69.5, 19.75, -70.5, 18.25)] // north-east → south-west
    [InlineData(-70.5, 19.75, -69.5, 18.25)] // north-west → south-east
    [InlineData(-69.5, 18.25, -70.5, 19.75)] // south-east → north-west
    public void Box_AnyTwoOppositeCorners_BecomeTheSameClosedFivePositionRectangle(double lon1, double lat1, double lon2, double lat2)
    {
        var request = MapSelectionRequest.FromGesture(Box([lon1, lat1], [lon2, lat2]));

        request.StrategyId.Should().Be("polygon-fence");
        request.Rings.Should().ContainSingle().Which.Should().BeEquivalentTo(_expectedRectangle, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Box_FromTheFivePositionRingGeomanDraws_IsTheSameRectangle()
    {
        var drawn = Box([-70.5, 18.25], [-70.5, 19.75], [-69.5, 19.75], [-69.5, 18.25], [-70.5, 18.25]);

        var request = MapSelectionRequest.FromGesture(drawn);

        request.Rings.Should().ContainSingle().Which.Should().BeEquivalentTo(_expectedRectangle, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Box_KeepsLongitudeFirst_ThenLatitude()
    {
        // Longitude -70 is not a valid latitude, so a swapped pair could not pass validation.
        var request = MapSelectionRequest.FromGesture(Box([-70, 18], [-69, 19]));

        request.Rings[0].Should().OnlyContain(position => position[0] <= -69 && position[1] >= 18);
    }

    [Fact]
    public void Request_CarriesOnlyTheRingsParameter_AsCoordinateArrays()
    {
        var request = MapSelectionRequest.FromGesture(Box([-70, 18], [-69, 19]));

        request.Parameters.Should().ContainSingle().Which.Key.Should().Be("rings");
        request.Parameters["rings"].Should().BeOfType<double[][][]>();
    }

    [Fact]
    public void Box_WithNoArea_IsRejected()
    {
        // A collapsed rectangle is an invalid ring to the validator before it is a zero-area one.
        var act = () => MapSelectionRequest.FromGesture(Box([-70, 18], [-70, 19]));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.SelfIntersecting);
    }

    [Fact]
    public void Box_WithNoPositions_IsRejectedAsEmpty()
    {
        var act = () => MapSelectionRequest.FromGesture(new MapSelectionGesture(MapSelectionTool.Box, [[]], false));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.Empty);
    }

    [Fact]
    public void Box_WithAMalformedPosition_IsRejected()
    {
        var act = () => MapSelectionRequest.FromGesture(Box([-70, 18, 5], [-69, 19]));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.MalformedPosition);
    }

    [Fact]
    public void Box_OutsideTheValidLongitudeRange_IsRejected()
    {
        var act = () => MapSelectionRequest.FromGesture(Box([179, 18], [181, 19]));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.OutOfRange);
    }

    [Fact]
    public void Fence_OpenRing_IsClosedWithACopyOfItsFirstPosition()
    {
        var request = MapSelectionRequest.FromGesture(Fence([-70, 18], [-69, 18], [-69, 19]));

        request.StrategyId.Should().Be("polygon-fence");
        request.Rings.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new double[][] { [-70, 18], [-69, 18], [-69, 19], [-70, 18] }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fence_AlreadyClosedRing_IsPassedThroughUnchanged()
    {
        double[][] ring = [[-70, 18], [-69, 18], [-69, 19], [-70, 18]];

        var request = MapSelectionRequest.FromGesture(Fence(ring));

        request.Rings.Should().ContainSingle().Which.Should().BeSameAs(ring);
    }

    [Fact]
    public void Fence_DoesNotMutateTheGesture()
    {
        double[][] ring = [[-70, 18], [-69, 18], [-69, 19]];

        MapSelectionRequest.FromGesture(Fence(ring));

        ring.Should().HaveCount(3);
    }

    [Fact]
    public void Fence_WithTwoPositions_IsRejectedAsTooFewVertices()
    {
        var act = () => MapSelectionRequest.FromGesture(Fence([-70, 18], [-69, 18]));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.TooFewVertices);
    }

    [Fact]
    public void Fence_WithMoreThanTwoThousandVertices_IsRejectedBeforeAnyRequest()
    {
        // A 2,000-position open circle closes to 2,001 vertices — one over the server's ceiling.
        var ring = Enumerable.Range(0, 2000)
            .Select(i => 2 * Math.PI * i / 2000)
            .Select(angle => new[] { -70 + Math.Cos(angle), 18 + Math.Sin(angle) })
            .ToArray();

        var act = () => MapSelectionRequest.FromGesture(Fence(ring));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.TooManyVertices);
    }

    [Fact]
    public void Fence_WithExactlyTwoThousandVerticesOnceClosed_IsAccepted()
    {
        var ring = Enumerable.Range(0, 1999)
            .Select(i => 2 * Math.PI * i / 1999)
            .Select(angle => new[] { -70 + Math.Cos(angle), 18 + Math.Sin(angle) })
            .ToArray();

        var request = MapSelectionRequest.FromGesture(Fence(ring));

        request.Rings[0].Should().HaveCount(2000);
    }

    [Fact]
    public void Fence_ThatCrossesItself_IsRejectedAsSelfIntersecting()
    {
        var act = () => MapSelectionRequest.FromGesture(Fence([-70, 18], [-69, 19], [-69, 18], [-70, 19]));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.SelfIntersecting);
    }

    [Fact]
    public void Fence_WithNoRings_IsRejectedAsEmpty()
    {
        var act = () => MapSelectionRequest.FromGesture(new MapSelectionGesture(MapSelectionTool.Fence, [], false));

        act.Should().Throw<InvalidSelectionFenceException>().Which.Defect.Should().Be(SelectionFenceDefect.Empty);
    }

    [Theory]
    [InlineData(MapSelectionTool.None)]
    [InlineData(MapSelectionTool.ClickToggle)]
    public void Gesture_OfANonAreaTool_ThrowsArgumentException(MapSelectionTool tool)
    {
        var act = () => MapSelectionRequest.FromGesture(new MapSelectionGesture(tool, [[[-70, 18], [-69, 19]]], false));

        act.Should().Throw<ArgumentException>().WithParameterName("gesture");
    }

    [Fact]
    public void Gesture_Null_ThrowsArgumentNullException()
    {
        var act = () => MapSelectionRequest.FromGesture(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("gesture");
    }

    [Fact]
    public Task UnavailableResolver_FailsEveryGestureWithNotSupported()
    {
        var act = () => new UnavailableMapSelectionResolver().ResolveAsync(Box([-70, 18], [-69, 19]), CancellationToken.None);

        return act.Should().ThrowAsync<NotSupportedException>();
    }
}
