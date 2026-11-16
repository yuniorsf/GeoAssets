using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services.FieldWork;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Isp.NeutralOperator;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.FieldWork;

/// <summary>
/// XD01-260's AC: "A completed census form validates against PoleSharingComplianceChecklistSchema
/// before local queuing."
/// </summary>
public class PoleCensusInspectionServiceTests
{
    private static GeoFeature PoleFeature() => new() { Id = "pole-1", Properties = new() { Name = "Pole 1" } };

    private static PoleCensusSubmission ValidSubmission() => new(
        StructuralCondition: "Fair",
        Band500MmCompliant: true,
        GroundToLowestCableMeters: 4.8,
        PowerToCommunicationBandMeters: 0.55,
        Occupants: [new PoleCensusOccupantSubmission(1, "AuthorizedContracted", "12345678000190")],
        UnauthorizedCableDetected: false);

    [Fact]
    public void CreateOrder_TargetsThePoleUnderThePoleCensusOrderType()
    {
        var pole = PoleFeature();
        var order = PoleCensusInspectionService.CreateOrder(pole, "tech-1", Guid.NewGuid(), TimeProvider.System);

        order.OrderTypeId.Should().Be(PoleCensusOrderTypes.PoleCensusInspectionId);
        order.Features.Should().ContainSingle().Which.Should().BeSameAs(pole);
        order.CreatedBy.Should().Be("tech-1");
        order.Status.Should().Be(FieldWorkWorkflowGraph.InitialStateKey);
    }

    [Fact]
    public void RecordSubmission_ValidSubmission_SucceedsAndEncodesSchemaConformantAttributes()
    {
        var order = PoleCensusInspectionService.CreateOrder(PoleFeature(), "tech-1", Guid.NewGuid(), TimeProvider.System);

        var result = PoleCensusInspectionService.RecordSubmission(order, ValidSubmission(), TimeProvider.System);

        result.Succeeded.Should().BeTrue();
        order.Attributes[PoleSharingComplianceChecklistSchema.StructuralConditionKey].Should().Be("Fair");
        order.Attributes[PoleSharingComplianceChecklistSchema.Band500MmCompliantKey].Should().Be("true");
        order.Attributes[PoleSharingComplianceChecklistSchema.ClearanceMeasurementsKey].Should().Contain("4.8");
        order.Attributes[PoleSharingComplianceChecklistSchema.OccupantsKey].Should().Contain("AuthorizedContracted");
        order.Attributes[PoleSharingComplianceChecklistSchema.UnauthorizedCableDetectedKey].Should().Be("false");
    }

    [Fact]
    public void RecordSubmission_ValidSubmission_AdvancesOrderToCompleted()
    {
        var order = PoleCensusInspectionService.CreateOrder(PoleFeature(), "tech-1", Guid.NewGuid(), TimeProvider.System);

        PoleCensusInspectionService.RecordSubmission(order, ValidSubmission(), TimeProvider.System);

        order.Status.Should().Be(FieldWorkStatus.Completed);
    }

    [Fact]
    public void RecordSubmission_InvalidStructuralCondition_FailsAndLeavesOrderAttributesUnset()
    {
        var order = PoleCensusInspectionService.CreateOrder(PoleFeature(), "tech-1", Guid.NewGuid(), TimeProvider.System);
        var invalid = ValidSubmission() with { StructuralCondition = "Dilapidated" };

        var result = PoleCensusInspectionService.RecordSubmission(order, invalid, TimeProvider.System);

        result.Succeeded.Should().BeFalse();
        result.Explanation.Should().NotBeNullOrEmpty();
        order.Attributes.Should().BeEmpty();
        order.Status.Should().Be(FieldWorkWorkflowGraph.InitialStateKey);
    }

    [Fact]
    public void RecordSubmission_TooManyOccupantSlots_Fails()
    {
        var order = PoleCensusInspectionService.CreateOrder(PoleFeature(), "tech-1", Guid.NewGuid(), TimeProvider.System);
        var occupants = Enumerable.Range(1, 7)
            .Select(i => new PoleCensusOccupantSubmission(i, "ActiveReservation"))
            .ToList();
        var invalid = ValidSubmission() with { Occupants = occupants };

        var result = PoleCensusInspectionService.RecordSubmission(order, invalid, TimeProvider.System);

        result.Succeeded.Should().BeFalse();
    }
}
