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

        SharedUseOrderTypes.RegisterAll(registry, ["pole-a", "pole-b"]);

        var poleWork = registry.Get(SharedUseOrderTypes.PoleWorkId);
        var notice = registry.Get(SharedUseOrderTypes.OccupantNoticeId);
        poleWork.TargetPolicy!.FanOut.Should().Be(new OrderTargetFanOutPolicy(SharedUseOrderTypes.OccupantNoticeId));
        poleWork.TargetPolicy.TargetAssetTypeIds.Should().Equal("pole-a", "pole-b");
        poleWork.TargetPolicy.Exclusivity.Should().Be(OrderTargetExclusivity.WarnOnOverlap);
        poleWork.TargetPolicy.SelectionLimit.Should().Be(SharedUseOrderTypes.PoleSelectionLimit);
        poleWork.TargetPolicy.AliasAttributeKey.Should().Be(SharedUseOrderTypes.PoleAliasAttributeKey);
        notice.TargetPolicy!.FanOut.Should().BeNull("a notice is not fanned out again");
        notice.TargetPolicy.TargetAssetTypeIds.Should().Equal("pole-a", "pole-b");
        notice.TargetPolicy.Exclusivity.Should().Be(OrderTargetExclusivity.None);
    }

    [Fact]
    public void BothTypes_OfferTheSameActivities_SoANoticeKeepsWhatItsParentAssigned()
    {
        var poleWork = SharedUseOrderTypes.CreatePoleWork(["pole"]);
        var notice = SharedUseOrderTypes.CreateOccupantNotice(["pole"]);

        poleWork.TargetPolicy!.Activities.Should().Equal(SharedUseOrderTypes.Activities);
        notice.TargetPolicy!.Activities.Should().Equal(SharedUseOrderTypes.Activities);
        SharedUseOrderTypes.Activities.Select(a => a.Id).Should().OnlyHaveUniqueItems().And.NotBeEmpty();
    }

    [Fact]
    public void PoleWork_StartsAsADraft_IsDispatchedFromDraftAndAgainOnceDispatched_AndFromNowhereElse()
    {
        var poleWork = SharedUseOrderTypes.CreatePoleWork(["pole"]);

        poleWork.InitialStateKey.Should().Be(ServiceOrderStatus.Draft);
        ServiceOrderTransitions.HasTransitionFor(poleWork, ServiceOrderStatus.Draft, OrderActionType.Dispatch).Should().BeTrue();
        ServiceOrderTransitions.HasTransitionFor(poleWork, ServiceOrderStatus.Pending, OrderActionType.Dispatch).Should().BeTrue();
        new[] { ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold, ServiceOrderStatus.Completed, ServiceOrderStatus.Cancelled }
            .Should().OnlyContain(status => !ServiceOrderTransitions.HasTransitionFor(poleWork, status, OrderActionType.Dispatch));
        ServiceOrderTransitions.IsTerminalState(poleWork, ServiceOrderStatus.Pending)
            .Should().BeFalse("dispatching again is not the only thing a dispatched order can do");
        ServiceOrderTransitions.IsTerminalState(poleWork, ServiceOrderStatus.Completed).Should().BeTrue();
        ServiceOrderTransitions.IsTerminalState(poleWork, ServiceOrderStatus.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void Notice_IsBornDispatched_AndIsItselfNeverDispatched()
    {
        var notice = SharedUseOrderTypes.CreateOccupantNotice(["pole"]);

        notice.InitialStateKey.Should().Be(ServiceOrderStatus.Pending);
        notice.States.Select(s => s.Key).Should().Contain(ServiceOrderStatus.Pending);
        notice.Transitions.Should().NotContain(t => t.TriggerAction == OrderActionType.Dispatch);
        ServiceOrderTransitions.IsValid(notice, ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress).Should().BeTrue();
        ServiceOrderTransitions.IsSuccessState(notice, ServiceOrderStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void PoleAssetTypes_AreTrimmedAndDeduplicated_BlanksDropped()
    {
        var poleWork = SharedUseOrderTypes.CreatePoleWork([" pole ", "POLE", "", "  ", "tower"]);

        poleWork.TargetPolicy!.TargetAssetTypeIds.Should().Equal("pole", "tower");
    }

    [Fact]
    public void Factories_WithNoPoleAssetType_ThrowArgumentException_AndNullThrowsArgumentNullException()
    {
        ((Action)(() => SharedUseOrderTypes.CreatePoleWork([]))).Should().Throw<ArgumentException>();
        ((Action)(() => SharedUseOrderTypes.CreateOccupantNotice([" "]))).Should().Throw<ArgumentException>();
        ((Action)(() => SharedUseOrderTypes.CreatePoleWork(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SharedUseOrderTypes.RegisterAll(null!, ["pole"]))).Should().Throw<ArgumentNullException>();
    }
}
