using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class FieldWorkOrderTransitionerTests
{
    private sealed class FakeGuard(bool canArrive = true, bool canStartWork = true) : IFieldWorkTransitionGuard
    {
        public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) => Task.FromResult(canArrive);
        public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(canStartWork);
    }

    private static readonly OrderType FieldWorkOrderType =
        FieldWorkWorkflowGraph.BuildOrderType("field-work", "Field Work Order");

    private static readonly GeoPoint SomeDevicePoint = new(-46.633, -23.55);

    [Fact]
    public async Task TransitionAsync_NoGuardSupplied_DefaultsToAlwaysAllowing()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.EnRoute };

        await FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.OnSite, TimeProvider.System, FieldWorkOrderType, devicePoint: SomeDevicePoint);

        order.Status.Should().Be(FieldWorkStatus.OnSite);
    }

    [Fact]
    public Task TransitionAsync_EnRouteToOnSite_NoDevicePointSupplied_ThrowsArgumentException()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.EnRoute };

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.OnSite, TimeProvider.System, FieldWorkOrderType);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TransitionAsync_EnRouteToOnSite_GuardRejectsArrival_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.EnRoute };
        var guard = new FakeGuard(canArrive: false);

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.OnSite, TimeProvider.System, FieldWorkOrderType, guard, SomeDevicePoint);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(FieldWorkStatus.EnRoute, "a rejected transition must never mutate the order");
    }

    [Fact]
    public Task TransitionAsync_OnSiteToInProgress_GuardRejectsStartWork_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite };
        var guard = new FakeGuard(canStartWork: false);

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, FieldWorkOrderType, guard);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public async Task TransitionAsync_OnSiteToInProgress_GuardAllowsStartWork_Succeeds()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite };
        var guard = new FakeGuard(canStartWork: true);

        await FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, FieldWorkOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.InProgress);
    }

    [Fact]
    public async Task TransitionAsync_UnguardedEdge_IgnoresARejectingGuardAndNeedsNoDevicePoint()
    {
        // A guard that rejects CanArrive/CanStartWork must never affect edges it doesn't own —
        // e.g. Draft -> Pending has nothing to do with arrival or starting work.
        var order = new ServiceOrder { Status = FieldWorkStatus.Draft };
        var guard = new FakeGuard(canArrive: false, canStartWork: false);

        await FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.Pending, TimeProvider.System, FieldWorkOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.Pending);
    }

    [Fact]
    public Task TransitionAsync_StructurallyIllegalEdge_ThrowsRegardlessOfGuard()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.Draft };
        var guard = new FakeGuard();

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.Completed, TimeProvider.System, FieldWorkOrderType, guard);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public async Task NullFieldWorkTransitionGuard_AllowsBothGuardedTransitions()
    {
        var order = new ServiceOrder();
        var guard = NullFieldWorkTransitionGuard.Instance;

        (await guard.CanArriveAsync(order, SomeDevicePoint)).Should().BeTrue();
        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }
}
