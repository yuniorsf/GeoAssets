using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary><see cref="OccupancyApproval"/> (XD01-318): which approvals commit staged occupancy, and where they lead.</summary>
public sealed class OccupancyApprovalTests
{
    private static readonly OrderType PoleWork = SharedUseOrderTypes.CreatePoleWork(["pole"]);

    [Fact]
    public void Commits_OnlyForATypeDeclaringATargetPolicy()
    {
        OccupancyApproval.Commits(null).Should().BeFalse();
        OccupancyApproval.Commits(new OrderType { Id = "plain", DisplayName = "Plain" }).Should().BeFalse();
        OccupancyApproval.Commits(new OrderType
        {
            Id = "survey",
            DisplayName = "Survey",
            Capabilities = [OccupancyCapabilities.Commit],
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] },
        }).Should().BeTrue();
        OccupancyApproval.Commits(PoleWork).Should().BeTrue();
    }

    [Fact]
    public void Commits_CapabilityAndTargets_True()
    {
        OccupancyApproval.Commits(TypeWith(OccupancyCapabilities.Commit, withTargets: true)).Should().BeTrue();
    }

    [Fact]
    public void Commits_TargetsWithoutCapability_False()
    {
        OccupancyApproval.Commits(TypeWith(capability: null, withTargets: true)).Should().BeFalse();
    }

    [Fact]
    public void Commits_CapabilityWithoutTargets_False()
    {
        OccupancyApproval.Commits(TypeWith(OccupancyCapabilities.Commit, withTargets: false)).Should().BeFalse();
    }

    [Fact]
    public void Commits_NullType_False()
    {
        OccupancyApproval.Commits(null).Should().BeFalse();
    }

    private static OrderType TypeWith(string? capability, bool withTargets) => new()
    {
        Id = "custom",
        DisplayName = "Custom",
        Capabilities = capability is null ? [] : [capability],
        TargetPolicy = withTargets ? new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] } : null,
    };

    [Fact]
    public void ApprovedStatus_IsTheTargetOfTheApprovalTransitionOutOfTheStatus()
    {
        OccupancyApproval.ApprovedStatus(PoleWork, ServiceOrderStatus.Completed).Should().Be(SharedUseOrderTypes.ApprovedStateKey);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    [InlineData(SharedUseOrderTypes.ApprovedStateKey)]
    [InlineData("Unknown")]
    public void ApprovedStatus_IsNull_FromAStatusWithNoApprovalTransition(string status)
    {
        OccupancyApproval.ApprovedStatus(PoleWork, status).Should().BeNull();
    }

    [Fact]
    public void ApprovedStatus_IsNull_ForNoTypeOrATypeWithoutAGraphOfItsOwn()
    {
        OccupancyApproval.ApprovedStatus(null, ServiceOrderStatus.Completed).Should().BeNull();
        OccupancyApproval.ApprovedStatus(new OrderType { Id = "plain", DisplayName = "Plain" }, ServiceOrderStatus.Completed).Should().BeNull();
    }

    [Fact]
    public void ApprovedStatus_IgnoresATransitionOutOfTheStatusThatIsNotAnApproval()
    {
        var type = new OrderType
        {
            Id = "review",
            DisplayName = "Review",
            States = [new("Review", "Review"), new("Done", "Done", IsSuccess: true), new("Rework", "Rework")],
            Transitions = [new("Review", "Rework", OrderActionType.Reject), new("Review", "Done", OrderActionType.Approve)],
        };

        OccupancyApproval.ApprovedStatus(type, "Review").Should().Be("Done");
        OccupancyApproval.ApprovedStatus(type, "Rework").Should().BeNull();
    }
}
