using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>XD01-274's AC: "Below −22.0 dBm at CTO auto-creates an upstream repair work order."</summary>
public class UpstreamCtoRepairOrderSpawnerTests
{
    private static readonly OrderType RepairOrderType = new() { Id = "cto-repair", DisplayName = "CTO Repair", InitialStateKey = "Open" };

    private static ServiceOrder DropOrder(string? scopeId = null) => new()
    {
        Id = "drop-1",
        Title = "Drop install — 123 Main St",
        OrganizationId = Guid.NewGuid(),
        CreatedBy = "tech-1",
        Attributes = scopeId is null ? [] : new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = scopeId },
    };

    [Fact]
    public void BuildIfNeeded_ResultDoesNotRequireRepair_ReturnsNull()
    {
        var passingResult = OpticalBudgetVerificationService.Verify(-18.0, -18.2);

        var repairOrder = UpstreamCtoRepairOrderSpawner.BuildIfNeeded(passingResult, DropOrder(), "cto-1", RepairOrderType, TimeProvider.System);

        repairOrder.Should().BeNull();
    }

    [Fact]
    public void BuildIfNeeded_CtoBelowMinimum_BuildsChildOrderParentedToDrop()
    {
        var failingResult = OpticalBudgetVerificationService.Verify(-23.0, -20.0);
        var dropOrder = DropOrder();

        var repairOrder = UpstreamCtoRepairOrderSpawner.BuildIfNeeded(failingResult, dropOrder, "cto-1", RepairOrderType, TimeProvider.System);

        repairOrder.Should().NotBeNull();
        repairOrder!.ParentOrderId.Should().Be(dropOrder.Id);
        repairOrder.OrderTypeId.Should().Be(RepairOrderType.Id);
        repairOrder.OrganizationId.Should().Be(dropOrder.OrganizationId);
        repairOrder.Status.Should().Be(RepairOrderType.InitialStateKey);
        repairOrder.Attributes["ctoFeatureId"].Should().Be("cto-1");
        repairOrder.Attributes["opmViolationDetail"].Should().Be(failingResult.ViolationDetail);
    }

    [Fact]
    public void BuildIfNeeded_ParentHasScopeId_ChildInheritsIt()
    {
        var failingResult = OpticalBudgetVerificationService.Verify(-23.0, -20.0);
        var dropOrder = DropOrder(scopeId: "municipio-123");

        var repairOrder = UpstreamCtoRepairOrderSpawner.BuildIfNeeded(failingResult, dropOrder, "cto-1", RepairOrderType, TimeProvider.System);

        repairOrder!.Attributes["scopeId"].Should().Be("municipio-123");
    }

    [Fact]
    public void BuildIfNeeded_ParentHasNoScopeId_ChildHasNoScopeIdAttribute()
    {
        var failingResult = OpticalBudgetVerificationService.Verify(-23.0, -20.0);

        var repairOrder = UpstreamCtoRepairOrderSpawner.BuildIfNeeded(failingResult, DropOrder(), "cto-1", RepairOrderType, TimeProvider.System);

        repairOrder!.Attributes.Should().NotContainKey("scopeId");
    }
}
