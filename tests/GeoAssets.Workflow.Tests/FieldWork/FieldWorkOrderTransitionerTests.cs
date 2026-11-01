using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class FieldWorkOrderTransitionerTests
{
    private sealed class FakeGuard(bool canArrive = true, bool canStartWork = true) : IFieldWorkTransitionGuard
    {
        public bool CanArrive(IServiceOrder order) => canArrive;
        public bool CanStartWork(IServiceOrder order) => canStartWork;
    }

    private static readonly OrderType FieldWorkOrderType =
        FieldWorkWorkflowGraph.BuildOrderType("field-work", "Field Work Order");

    [Fact]
    public void Transition_NoGuardSupplied_DefaultsToAlwaysAllowing()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.EnRoute };

        FieldWorkOrderTransitioner.Transition(order, FieldWorkStatus.OnSite, TimeProvider.System, FieldWorkOrderType);

        order.Status.Should().Be(FieldWorkStatus.OnSite);
    }

    [Fact]
    public void Transition_EnRouteToOnSite_GuardRejectsArrival_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.EnRoute };
        var guard = new FakeGuard(canArrive: false);

        var act = () => FieldWorkOrderTransitioner.Transition(order, FieldWorkStatus.OnSite, TimeProvider.System, FieldWorkOrderType, guard);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(FieldWorkStatus.EnRoute, "a rejected transition must never mutate the order");
    }

    [Fact]
    public void Transition_OnSiteToInProgress_GuardRejectsStartWork_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite };
        var guard = new FakeGuard(canStartWork: false);

        var act = () => FieldWorkOrderTransitioner.Transition(order, FieldWorkStatus.InProgress, TimeProvider.System, FieldWorkOrderType, guard);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public void Transition_OnSiteToInProgress_GuardAllowsStartWork_Succeeds()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite };
        var guard = new FakeGuard(canStartWork: true);

        FieldWorkOrderTransitioner.Transition(order, FieldWorkStatus.InProgress, TimeProvider.System, FieldWorkOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.InProgress);
    }

    [Fact]
    public void Transition_UnguardedEdge_IgnoresARejectingGuard()
    {
        // A guard that rejects CanArrive/CanStartWork must never affect edges it doesn't own —
        // e.g. Draft -> Pending has nothing to do with arrival or starting work.
        var order = new ServiceOrder { Status = FieldWorkStatus.Draft };
        var guard = new FakeGuard(canArrive: false, canStartWork: false);

        FieldWorkOrderTransitioner.Transition(order, FieldWorkStatus.Pending, TimeProvider.System, FieldWorkOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.Pending);
    }

    [Fact]
    public void Transition_StructurallyIllegalEdge_ThrowsRegardlessOfGuard()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.Draft };
        var guard = new FakeGuard();

        var act = () => FieldWorkOrderTransitioner.Transition(order, FieldWorkStatus.Completed, TimeProvider.System, FieldWorkOrderType, guard);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public void NullFieldWorkTransitionGuard_AllowsBothGuardedTransitions()
    {
        var order = new ServiceOrder();
        var guard = NullFieldWorkTransitionGuard.Instance;

        guard.CanArrive(order).Should().BeTrue();
        guard.CanStartWork(order).Should().BeTrue();
    }
}
