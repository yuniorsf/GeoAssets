using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class CompositeFieldWorkTransitionGuardTests
{
    private sealed class FakeGuard(bool canArrive = true, bool canStartWork = true, bool canComplete = true) : IFieldWorkTransitionGuard
    {
        public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) => Task.FromResult(canArrive);
        public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(canStartWork);
        public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(canComplete);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CanArriveAsync_DelegatesOnlyToTheArrivalGuard(bool arrivalGuardResult, bool safetyGuardArrivalResultThatMustBeIgnored)
    {
        var composite = new CompositeFieldWorkTransitionGuard(
            new FakeGuard(canArrive: arrivalGuardResult),
            new FakeGuard(canArrive: safetyGuardArrivalResultThatMustBeIgnored),
            new FakeGuard());

        (await composite.CanArriveAsync(new ServiceOrder(), new GeoPoint(0, 0))).Should().Be(arrivalGuardResult);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public async Task CanStartWorkAsync_RequiresBothSafetyAndPhotoGatingGuards(bool safetyResult, bool photoGatingResult, bool expected)
    {
        var composite = new CompositeFieldWorkTransitionGuard(
            new FakeGuard(canStartWork: true), // arrival guard's CanStartWorkAsync must be ignored
            new FakeGuard(canStartWork: safetyResult),
            new FakeGuard(canStartWork: photoGatingResult));

        (await composite.CanStartWorkAsync(new ServiceOrder())).Should().Be(expected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CanCompleteAsync_DelegatesOnlyToThePhotoGatingGuard(bool photoGatingResult, bool othersResultThatMustBeIgnored)
    {
        var composite = new CompositeFieldWorkTransitionGuard(
            new FakeGuard(canComplete: othersResultThatMustBeIgnored),
            new FakeGuard(canComplete: othersResultThatMustBeIgnored),
            new FakeGuard(canComplete: photoGatingResult));

        (await composite.CanCompleteAsync(new ServiceOrder())).Should().Be(photoGatingResult);
    }
}
