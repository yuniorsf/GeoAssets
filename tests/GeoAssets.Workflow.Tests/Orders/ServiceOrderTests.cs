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

    [Fact]
    public void RecordAction_DefaultActorKind_IsHuman()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(OrderActionType.Annotate, "tech-1");

        order.ActionLog.Single().ActorKind.Should().Be(ActorKind.Human);
    }

    [Fact]
    public void RecordAction_AgentActor_RecordsActorKindAndInvocationId()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(
            OrderActionType.Annotate,
            "agent-hydro-01",
            actorKind: ActorKind.Agent,
            agentInvocationId: "run-42");

        var entry = order.ActionLog.Single();
        entry.ActorKind.Should().Be(ActorKind.Agent);
        entry.AgentInvocationId.Should().Be("run-42");
    }

    // ── DispatchTo ─────────────────────────────────────────────────────────────

    [Fact]
    public void DispatchTo_DefaultActorKind_IsHuman()
    {
        var order = new ServiceOrder();

        order.DispatchTo("tech-1", DispatchTargetType.User, "supervisor-1");

        order.Dispatches.Single().ActorKind.Should().Be(ActorKind.Human);
        order.ActionLog.Single().ActorKind.Should().Be(ActorKind.Human);
    }

    [Fact]
    public void DispatchTo_AgentActor_RecordsActorKindAndInvocationIdOnDispatchAndActionLog()
    {
        var order = new ServiceOrder();

        order.DispatchTo(
            "tech-1",
            DispatchTargetType.User,
            "agent-dispatcher-01",
            actorKind: ActorKind.Agent,
            agentInvocationId: "run-7");

        order.Dispatches.Single().Should().Match<OrderDispatch>(
            d => d.ActorKind == ActorKind.Agent && d.AgentInvocationId == "run-7");
        order.ActionLog.Single().Should().Match<OrderActionLog>(
            a => a.ActorKind == ActorKind.Agent && a.AgentInvocationId == "run-7");
    }
}
