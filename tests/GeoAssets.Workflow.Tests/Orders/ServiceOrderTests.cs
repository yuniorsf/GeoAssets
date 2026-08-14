using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class ServiceOrderTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ── Transition ─────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_LegalTransition_UpdatesStatus()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.Transition(ServiceOrderStatus.Pending, TimeProvider.System);

        order.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public void Transition_ToCompleted_SetsCompletedAt()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.InProgress };

        order.Transition(ServiceOrderStatus.Completed, TimeProvider.System);

        order.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Transition_StampsUpdatedAtAndCompletedAt_FromInjectedTimeProvider()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.InProgress };
        var fixedNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        order.Transition(ServiceOrderStatus.Completed, new FixedTimeProvider(fixedNow));

        order.UpdatedAt.Should().Be(fixedNow.UtcDateTime);
        order.CompletedAt.Should().Be(fixedNow.UtcDateTime);
    }

    [Fact]
    public void Transition_IllegalTransition_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        var act = () => order.Transition(ServiceOrderStatus.Completed, TimeProvider.System);

        act.Should().Throw<InvalidServiceOrderTransitionException>()
            .Which.Should().Match<InvalidServiceOrderTransitionException>(
                e => e.From == ServiceOrderStatus.Draft && e.To == ServiceOrderStatus.Completed);
    }

    [Fact]
    public void Transition_IllegalTransition_DoesNotMutateStatus()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        try { order.Transition(ServiceOrderStatus.Completed, TimeProvider.System); } catch (InvalidServiceOrderTransitionException) { }

        order.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    // ── RecordAction ───────────────────────────────────────────────────────────

    [Fact]
    public void RecordAction_LegalResultingStatus_TransitionsOrder()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(OrderActionType.Approve, "supervisor-1", TimeProvider.System, resultingStatus: ServiceOrderStatus.Pending);

        order.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public void RecordAction_WithoutResultingStatus_DoesNotTransition()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(OrderActionType.Annotate, "tech-1", TimeProvider.System, comment: "note");

        order.Status.Should().Be(ServiceOrderStatus.Draft);
        order.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public void RecordAction_IllegalResultingStatus_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        var act = () => order.RecordAction(OrderActionType.Approve, "supervisor-1", TimeProvider.System, resultingStatus: ServiceOrderStatus.Completed);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public void RecordAction_DefaultActorKind_IsHuman()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(OrderActionType.Annotate, "tech-1", TimeProvider.System);

        order.ActionLog.Single().ActorKind.Should().Be(ActorKind.Human);
    }

    [Fact]
    public void RecordAction_AgentActor_RecordsActorKindAndInvocationId()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };

        order.RecordAction(
            OrderActionType.Annotate,
            "agent-hydro-01",
            TimeProvider.System,
            actorKind: ActorKind.Agent,
            agentInvocationId: "run-42");

        var entry = order.ActionLog.Single();
        entry.ActorKind.Should().Be(ActorKind.Agent);
        entry.AgentInvocationId.Should().Be("run-42");
    }

    [Fact]
    public void RecordAction_StampsActionLogAndUpdatedAt_FromInjectedTimeProvider()
    {
        var order = new ServiceOrder { Status = ServiceOrderStatus.Draft };
        var fixedNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        order.RecordAction(OrderActionType.Annotate, "tech-1", new FixedTimeProvider(fixedNow), comment: "note");

        order.ActionLog.Single().PerformedAt.Should().Be(fixedNow.UtcDateTime);
        order.UpdatedAt.Should().Be(fixedNow.UtcDateTime);
    }

    // ── DispatchTo ─────────────────────────────────────────────────────────────

    [Fact]
    public void DispatchTo_DefaultActorKind_IsHuman()
    {
        var order = new ServiceOrder();

        order.DispatchTo("tech-1", DispatchTargetType.User, "supervisor-1", TimeProvider.System);

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
            TimeProvider.System,
            actorKind: ActorKind.Agent,
            agentInvocationId: "run-7");

        order.Dispatches.Single().Should().Match<OrderDispatch>(
            d => d.ActorKind == ActorKind.Agent && d.AgentInvocationId == "run-7");
        order.ActionLog.Single().Should().Match<OrderActionLog>(
            a => a.ActorKind == ActorKind.Agent && a.AgentInvocationId == "run-7");
    }

    [Fact]
    public void DispatchTo_StampsDispatchedAtAndUpdatedAt_FromInjectedTimeProvider()
    {
        var order = new ServiceOrder();
        var fixedNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        order.DispatchTo("tech-1", DispatchTargetType.User, "supervisor-1", new FixedTimeProvider(fixedNow));

        order.Dispatches.Single().DispatchedAt.Should().Be(fixedNow.UtcDateTime);
        order.UpdatedAt.Should().Be(fixedNow.UtcDateTime);
    }

    // ── WithFeatures ───────────────────────────────────────────────────────────

    [Fact]
    public void WithFeatures_ReplacesFeaturesAndRecordsSpec()
    {
        var order = new ServiceOrder();
        var spec  = new FeatureSelectionSpec { StrategyId = "bounding-box", ExecutedAt = TimeProvider.System.GetUtcNow().UtcDateTime };

        order.WithFeatures([new GeoFeature { Id = "f1" }], TimeProvider.System, spec);

        order.Features.Should().ContainSingle(f => f.Id == "f1");
        order.SelectionSpec.Should().Be(spec);
    }

    [Fact]
    public void WithFeatures_StampsUpdatedAt_FromInjectedTimeProvider()
    {
        var order = new ServiceOrder();
        var fixedNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        order.WithFeatures([], new FixedTimeProvider(fixedNow));

        order.UpdatedAt.Should().Be(fixedNow.UtcDateTime);
    }
}
