using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class CompositeFieldWorkTransitionGuardTests
{
    private sealed class FakeGuard(bool canArrive, bool canStartWork) : IFieldWorkTransitionGuard
    {
        public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) => Task.FromResult(canArrive);
        public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(canStartWork);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CanArriveAsync_DelegatesOnlyToTheArrivalGuard(bool arrivalGuardResult, bool safetyGuardArrivalResultThatMustBeIgnored)
    {
        var composite = new CompositeFieldWorkTransitionGuard(
            new FakeGuard(canArrive: arrivalGuardResult, canStartWork: true),
            new FakeGuard(canArrive: safetyGuardArrivalResultThatMustBeIgnored, canStartWork: true));

        (await composite.CanArriveAsync(new ServiceOrder(), new GeoPoint(0, 0))).Should().Be(arrivalGuardResult);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CanStartWorkAsync_DelegatesOnlyToTheSafetyGuard(bool safetyGuardResult, bool arrivalGuardStartWorkResultThatMustBeIgnored)
    {
        var composite = new CompositeFieldWorkTransitionGuard(
            new FakeGuard(canArrive: true, canStartWork: arrivalGuardStartWorkResultThatMustBeIgnored),
            new FakeGuard(canArrive: true, canStartWork: safetyGuardResult));

        (await composite.CanStartWorkAsync(new ServiceOrder())).Should().Be(safetyGuardResult);
    }
}
