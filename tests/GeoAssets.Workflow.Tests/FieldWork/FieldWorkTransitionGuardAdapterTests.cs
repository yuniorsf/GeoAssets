using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class FieldWorkTransitionGuardAdapterTests
{
    private sealed class FakeInnerGuard(bool canStartWork = true, bool canComplete = true) : IFieldWorkTransitionGuard
    {
        public int Calls { get; private set; }

        public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(true);
        }

        public Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(canStartWork);
        }

        public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(canComplete);
        }
    }

    private static OrderTransitionContext Context(string from, string to) =>
        new(new ServiceOrder { Id = "o" }, null, from, to, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task OnSiteToInProgress_StartAllowed_Allows()
    {
        var sut = new FieldWorkTransitionGuardAdapter(new FakeInnerGuard(canStartWork: true));

        var verdict = await sut.EvaluateAsync(Context(FieldWorkStatus.OnSite, FieldWorkStatus.InProgress), CancellationToken.None);

        verdict.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task OnSiteToInProgress_StartDenied_DeniesWithStartCode()
    {
        var sut = new FieldWorkTransitionGuardAdapter(new FakeInnerGuard(canStartWork: false, canComplete: true));

        var verdict = await sut.EvaluateAsync(Context(FieldWorkStatus.OnSite, FieldWorkStatus.InProgress), CancellationToken.None);

        verdict.Allowed.Should().BeFalse();
        verdict.Code.Should().Be(FieldWorkTransitionGuardAdapter.StartWorkDeniedCode);
        verdict.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InProgressToCompleted_CompleteAllowed_Allows()
    {
        var sut = new FieldWorkTransitionGuardAdapter(new FakeInnerGuard(canComplete: true));

        var verdict = await sut.EvaluateAsync(Context(FieldWorkStatus.InProgress, FieldWorkStatus.Completed), CancellationToken.None);

        verdict.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task InProgressToCompleted_CompleteDenied_DeniesWithCompletionCode()
    {
        var sut = new FieldWorkTransitionGuardAdapter(new FakeInnerGuard(canStartWork: true, canComplete: false));

        var verdict = await sut.EvaluateAsync(Context(FieldWorkStatus.InProgress, FieldWorkStatus.Completed), CancellationToken.None);

        verdict.Allowed.Should().BeFalse();
        verdict.Code.Should().Be(FieldWorkTransitionGuardAdapter.CompletionDeniedCode);
    }

    [Theory]
    [InlineData(FieldWorkStatus.InProgress, FieldWorkStatus.OnSite)]
    [InlineData(FieldWorkStatus.OnSite, FieldWorkStatus.Completed)]
    [InlineData(FieldWorkStatus.Completed, FieldWorkStatus.InProgress)]
    [InlineData("Draft", "Pending")]
    public async Task EveryOtherEdge_IsAllowed_WithoutCallingTheInnerGuard(string from, string to)
    {
        var inner = new FakeInnerGuard(canStartWork: false, canComplete: false);
        var sut = new FieldWorkTransitionGuardAdapter(inner);

        var verdict = await sut.EvaluateAsync(Context(from, to), CancellationToken.None);

        verdict.Allowed.Should().BeTrue();
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public Task EvaluateAsync_NullContext_Throws()
    {
        var sut = new FieldWorkTransitionGuardAdapter(new FakeInnerGuard());

        var act = () => sut.EvaluateAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }
}
