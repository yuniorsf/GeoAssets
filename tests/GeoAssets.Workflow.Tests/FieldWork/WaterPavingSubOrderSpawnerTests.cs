using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Domain.Sanitation.Icerp;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>XD01-230's two stated ACs, against the real geodesic area calculator (not a stub).</summary>
public class WaterPavingSubOrderSpawnerTests
{
    // Must match GeodeticAreaCalculatorTests' own constant, for the same self-consistency reason.
    private const double MetersPerDegreeLatitude = 111_320.0;

    private static readonly OrderType ParentOrderType = FieldWorkWorkflowGraph.BuildOrderType("water-sewer-repair", "Water/Sewer Repair");
    private static readonly OrderType PavingOrderType = FieldWorkWorkflowGraph.BuildOrderType("water-paving-restoration", "Water Paving Restoration");

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

    private static ServiceOrder ParentOrder() => new()
    {
        Title = "Main break repair",
        Status = FieldWorkStatus.Completed,
        OrderTypeId = ParentOrderType.Id,
        OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        CreatedBy = "crew-1",
    };

    [Fact]
    public void TransitionToInspectedWithPavingSubOrder_GeometryWithinTolerance_CreatesChildAndTransitionsParentTogether()
    {
        var parent = ParentOrder();
        var boundary = SquareOfSide(-46.633, -23.55, sideMeters: 10); // actual area = 100 m²
        var recordedTrench = new TrenchDimensions(LengthMeters: 10, WidthMeters: 10, DepthMeters: 1.5); // area = 100 m², no divergence

        var child = WaterPavingSubOrderSpawner.TransitionToInspectedWithPavingSubOrder(
            parent, recordedTrench, boundary, ParentOrderType, PavingOrderType, TimeProvider.System);

        parent.Status.Should().Be(FieldWorkStatus.Inspected, "the parent transition and child creation happen together");
        child.ParentOrderId.Should().Be(parent.Id);
        child.OrderTypeId.Should().Be(PavingOrderType.Id);
        child.OrganizationId.Should().Be(parent.OrganizationId);
        child.Attributes["trenchVolumeCubicMeters"].Should().Be((100.0 * 1.5).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TransitionToInspectedWithPavingSubOrder_DivergenceExactlyAtFifteenPercent_Succeeds()
    {
        var parent = ParentOrder();
        var boundary = SquareOfSide(-46.633, -23.55, sideMeters: 10); // actual area = 100 m²
        var recordedTrench = new TrenchDimensions(LengthMeters: 11.5, WidthMeters: 10); // area = 115 m² -> exactly +15%

        var act = () => WaterPavingSubOrderSpawner.TransitionToInspectedWithPavingSubOrder(
            parent, recordedTrench, boundary, ParentOrderType, PavingOrderType, TimeProvider.System);

        act.Should().NotThrow("the 15% boundary itself must pass, matching this codebase's other boundary-inclusive predicates");
        parent.Status.Should().Be(FieldWorkStatus.Inspected);
    }

    [Fact]
    public void TransitionToInspectedWithPavingSubOrder_DivergenceJustOverFifteenPercent_ThrowsAndLeavesParentUntouched()
    {
        var parent = ParentOrder();
        var boundary = SquareOfSide(-46.633, -23.55, sideMeters: 10); // actual area = 100 m²
        var recordedTrench = new TrenchDimensions(LengthMeters: 11.501, WidthMeters: 10); // area = 115.01 m² -> just over +15%

        var act = () => WaterPavingSubOrderSpawner.TransitionToInspectedWithPavingSubOrder(
            parent, recordedTrench, boundary, ParentOrderType, PavingOrderType, TimeProvider.System);

        act.Should().Throw<TrenchGeometryDivergenceException>();
        parent.Status.Should().Be(FieldWorkStatus.Completed, "a rejected geometry check must never partially commit the parent's transition");
    }

    [Fact]
    public void TransitionToInspectedWithPavingSubOrder_RecordedAreaTooSmall_AlsoRejectsBeyondFifteenPercentUnder()
    {
        var parent = ParentOrder();
        var boundary = SquareOfSide(-46.633, -23.55, sideMeters: 10); // actual area = 100 m²
        var recordedTrench = new TrenchDimensions(LengthMeters: 8.49, WidthMeters: 10); // area = 84.9 m² -> just over -15% under

        var act = () => WaterPavingSubOrderSpawner.TransitionToInspectedWithPavingSubOrder(
            parent, recordedTrench, boundary, ParentOrderType, PavingOrderType, TimeProvider.System);

        act.Should().Throw<TrenchGeometryDivergenceException>();
    }

    [Theory]
    [InlineData(115, 100, true)]   // exactly +15%
    [InlineData(85, 100, true)]    // exactly -15%
    [InlineData(115.01, 100, false)]
    [InlineData(84.99, 100, false)]
    [InlineData(100, 0, false)]    // no real boundary to compare against
    public void IsWithinDivergenceTolerance_MatchesExpectedBoundaryBehavior(double recorded, double actual, bool expected) =>
        WaterPavingSubOrderSpawner.IsWithinDivergenceTolerance(recorded, actual).Should().Be(expected);
}
