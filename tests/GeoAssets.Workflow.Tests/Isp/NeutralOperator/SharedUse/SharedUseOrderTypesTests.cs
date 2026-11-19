using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator.SharedUse;

public sealed class SharedUseOrderTypesTests
{
    [Fact]
    public void RegisterAll_RegistersThePoleWorkTypeAndTheNoticeItFansOutInto()
    {
        var registry = new OrderTypeRegistry();

        SharedUseOrderTypes.RegisterAll(registry);

        var poleWork = registry.Get(SharedUseOrderTypes.PoleWorkId);
        var notice = registry.Get(SharedUseOrderTypes.OccupantNoticeId);
        poleWork.TargetPolicy!.FanOut.Should().Be(new OrderTargetFanOutPolicy(SharedUseOrderTypes.OccupantNoticeId));
        poleWork.TargetPolicy.TargetAssetTypeRoles.Should().Equal("occupiable-support");
        poleWork.TargetPolicy.TargetAssetTypeIds.Should().BeEmpty();
        poleWork.TargetPolicy.Exclusivity.Should().Be(OrderTargetExclusivity.WarnOnOverlap);
        poleWork.TargetPolicy.SelectionLimit.Should().Be(SharedUseOrderTypes.PoleSelectionLimit);
        poleWork.TargetPolicy.AliasAttributeKey.Should().Be(SharedUseOrderTypes.PoleAliasAttributeKey);
        notice.TargetPolicy!.FanOut.Should().BeNull("a notice is not fanned out again");
        notice.TargetPolicy.TargetAssetTypeRoles.Should().Equal("occupiable-support");
        notice.TargetPolicy.TargetAssetTypeIds.Should().BeEmpty();
        notice.TargetPolicy.Exclusivity.Should().Be(OrderTargetExclusivity.None);
    }

    [Fact]
    public void BothTypes_OfferTheSameActivities_SoANoticeKeepsWhatItsParentAssigned()
    {
        var poleWork = SharedUseOrderTypes.CreatePoleWork();
        var notice = SharedUseOrderTypes.CreateOccupantNotice();

        poleWork.TargetPolicy!.Activities.Should().Equal(SharedUseOrderTypes.Activities);
        notice.TargetPolicy!.Activities.Should().Equal(SharedUseOrderTypes.Activities);
        SharedUseOrderTypes.Activities.Select(a => a.Id).Should().OnlyHaveUniqueItems().And.NotBeEmpty();
    }

    [Fact]
    public void PoleWork_StartsAsADraft_IsDispatchedFromDraftAndAgainOnceDispatched_AndFromNowhereElse()
    {
        var poleWork = SharedUseOrderTypes.CreatePoleWork();

        poleWork.InitialStateKey.Should().Be(ServiceOrderStatus.Draft);
        ServiceOrderTransitions.HasTransitionFor(poleWork, ServiceOrderStatus.Draft, OrderActionType.Dispatch).Should().BeTrue();
        ServiceOrderTransitions.HasTransitionFor(poleWork, ServiceOrderStatus.Pending, OrderActionType.Dispatch).Should().BeTrue();
        new[] { ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold, ServiceOrderStatus.Completed, ServiceOrderStatus.Cancelled }
            .Should().OnlyContain(status => !ServiceOrderTransitions.HasTransitionFor(poleWork, status, OrderActionType.Dispatch));
        ServiceOrderTransitions.IsTerminalState(poleWork, ServiceOrderStatus.Pending)
            .Should().BeFalse("dispatching again is not the only thing a dispatched order can do");
        ServiceOrderTransitions.IsTerminalState(poleWork, ServiceOrderStatus.Completed)
            .Should().BeFalse("completed pole work waits for its approval (XD01-318)");
        ServiceOrderTransitions.IsTerminalState(poleWork, SharedUseOrderTypes.ApprovedStateKey).Should().BeTrue();
        ServiceOrderTransitions.IsTerminalState(poleWork, ServiceOrderStatus.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void PoleWork_IsApprovedOnlyOnceCompleted_AndApprovedIsItsOnlySuccess_WhileTheNoticeStillSucceedsOnCompletion()
    {
        var poleWork = SharedUseOrderTypes.CreatePoleWork();
        var notice = SharedUseOrderTypes.CreateOccupantNotice();

        poleWork.Transitions.Where(t => t.TriggerAction == OrderActionType.Approve)
            .Should().ContainSingle().Which.Should().Be(new WorkflowTransition(ServiceOrderStatus.Completed, SharedUseOrderTypes.ApprovedStateKey, OrderActionType.Approve));
        poleWork.States.Where(s => s.IsSuccess).Select(s => s.Key).Should().Equal(SharedUseOrderTypes.ApprovedStateKey);
        ServiceOrderTransitions.IsValid(poleWork, ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed).Should().BeTrue();
        ServiceOrderTransitions.HasTransitionFor(poleWork, ServiceOrderStatus.Completed, OrderActionType.Reject).Should().BeTrue();
        ServiceOrderTransitions.IsValid(poleWork, ServiceOrderStatus.Completed, ServiceOrderStatus.InProgress).Should().BeTrue("a rejected approval sends the work back to the crew");
        ServiceOrderTransitions.HasTransitionFor(poleWork, ServiceOrderStatus.Completed, OrderActionType.Cancel).Should().BeTrue();
        notice.Transitions.Should().NotContain(t => t.TriggerAction == OrderActionType.Approve);
        notice.States.Select(s => s.Key).Should().NotContain(SharedUseOrderTypes.ApprovedStateKey);
    }

    [Fact]
    public void Notice_IsBornDispatched_AndIsItselfNeverDispatched()
    {
        var notice = SharedUseOrderTypes.CreateOccupantNotice();

        notice.InitialStateKey.Should().Be(ServiceOrderStatus.Pending);
        notice.States.Select(s => s.Key).Should().Contain(ServiceOrderStatus.Pending);
        notice.Transitions.Should().NotContain(t => t.TriggerAction == OrderActionType.Dispatch);
        ServiceOrderTransitions.IsValid(notice, ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress).Should().BeTrue();
        ServiceOrderTransitions.IsSuccessState(notice, ServiceOrderStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void BothTypes_TargetTheOccupiableSupportRole_AndNameNoAssetTypeId()
    {
        foreach (var type in new[] { SharedUseOrderTypes.CreatePoleWork(), SharedUseOrderTypes.CreateOccupantNotice() })
        {
            type.TargetPolicy!.TargetAssetTypeRoles.Should().Equal("occupiable-support");
            type.TargetPolicy.TargetAssetTypeIds.Should().BeEmpty();
            ((Action)(() => type.TargetPolicy.Validate(type.Id))).Should().NotThrow();
        }
    }

    [Fact]
    public void RegisterAll_WithANullRegistry_ThrowsArgumentNullException() =>
        ((Action)(() => SharedUseOrderTypes.RegisterAll(null!))).Should().Throw<ArgumentNullException>();
}
