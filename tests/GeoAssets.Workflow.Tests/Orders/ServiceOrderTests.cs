using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class ServiceOrderTests
{
    // ── Transition ─────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_LegalTransition_UpdatesStatus()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.Transition(ServiceOrderStatus.Pending);

        order.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public void Transition_ToCompleted_SetsCompletedAt()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.InProgress };

        order.Transition(ServiceOrderStatus.Completed);

        order.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Transition_IllegalTransition_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        var act = () => order.Transition(ServiceOrderStatus.Completed);

        act.Should().Throw<InvalidServiceOrderTransitionException>()
            .Which.Should().Match<InvalidServiceOrderTransitionException>(
                e => e.From == ServiceOrderStatus.Draft && e.To == ServiceOrderStatus.Completed);
    }

    [Fact]
    public void Transition_IllegalTransition_DoesNotMutateStatus()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        try { order.Transition(ServiceOrderStatus.Completed); } catch (InvalidServiceOrderTransitionException) { }

        order.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    // ── RecordAction ───────────────────────────────────────────────────────────

    [Fact]
    public void RecordAction_LegalResultingStatus_TransitionsOrder()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(OrderActionType.Approve, "supervisor-1", resultingStatus: ServiceOrderStatus.Pending);

        order.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public void RecordAction_WithoutResultingStatus_DoesNotTransition()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(OrderActionType.Annotate, "tech-1", comment: "note");

        order.Status.Should().Be(ServiceOrderStatus.Draft);
        order.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public void RecordAction_IllegalResultingStatus_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        var act = () => order.RecordAction(OrderActionType.Approve, "supervisor-1", resultingStatus: ServiceOrderStatus.Completed);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }
}
