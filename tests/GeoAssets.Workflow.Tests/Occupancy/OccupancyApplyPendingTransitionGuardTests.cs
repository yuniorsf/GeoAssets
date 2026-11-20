using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary>
/// <see cref="OccupancyApplyPendingTransitionGuard"/> (XD01-383): an order that is "approved, awaiting apply" changes
/// status only along its approval transition — everything else waits for the asset catalog's answer — and an order that is
/// not awaiting is never held by it.
/// </summary>
public sealed class OccupancyApplyPendingTransitionGuardTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly OrderType _poleWork = PoleWork();
    private readonly OccupancyApplyPendingTransitionGuard _sut = new();

    [Theory]
    [InlineData(ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Draft)]
    public async Task EvaluateAsync_AnyOtherTransitionOfAnAwaitingOrder_IsDeniedAsApplyPending(string to)
    {
        var verdict = await _sut.EvaluateAsync(Context(Order(OccupancyApplySagaAttributes.AwaitingApply), ServiceOrderStatus.Completed, to), CancellationToken.None);

        verdict.Allowed.Should().BeFalse();
        verdict.Code.Should().Be(OccupancyProblemCodes.ApplyPending).And.Be(OccupancyApplyPendingTransitionGuard.ApplyPendingCode);
        verdict.Reason.Should().Contain("awaits the asset catalog");
    }

    [Fact]
    public async Task EvaluateAsync_TheApprovalTransitionOfAnAwaitingOrder_IsAllowed_SoTheSagaCanFinalizeIt()
    {
        var approved = OccupancyApproval.ApprovedStatus(_poleWork, ServiceOrderStatus.Completed)!;

        var verdict = await _sut.EvaluateAsync(Context(Order(OccupancyApplySagaAttributes.AwaitingApply), ServiceOrderStatus.Completed, approved), CancellationToken.None);

        verdict.Should().BeSameAs(GuardVerdict.Allow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OccupancyApplySagaAttributes.Applied)]
    [InlineData(OccupancyApplySagaAttributes.Reverted)]
    public async Task EvaluateAsync_AnOrderThatIsNotAwaiting_IsNeverHeld(string? state)
    {
        var order = Order(state);

        foreach (var to in new[] { ServiceOrderStatus.Cancelled, ServiceOrderStatus.InProgress, ServiceOrderStatus.Draft })
        {
            var verdict = await _sut.EvaluateAsync(Context(order, ServiceOrderStatus.Completed, to), CancellationToken.None);
            verdict.Should().BeSameAs(GuardVerdict.Allow, $"{to} is not held when the state is '{state}'");
        }
    }

    [Fact]
    public Task EvaluateAsync_NullContext_ThrowsArgumentNullException()
    {
        var act = () => _sut.EvaluateAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("awaiting-apply", true)]
    [InlineData("applied", false)]
    [InlineData("reverted", false)]
    [InlineData("AWAITING-APPLY", false)]
    public void IsAwaitingApply_IsTrueOnlyForTheExactAwaitingState(string? state, bool expected)
    {
        var attributes = state is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { [OccupancyApplySagaAttributes.StateKey] = state };

        OccupancyApplySagaAttributes.IsAwaitingApply(attributes).Should().Be(expected);
    }

    [Fact]
    public void IsAwaitingApply_NullAttributes_ThrowsArgumentNullException()
    {
        var act = () => OccupancyApplySagaAttributes.IsAwaitingApply(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private OrderTransitionContext Context(ServiceOrder order, string from, string to) => new(order, _poleWork, from, to, At);

    private static ServiceOrder Order(string? sagaState) => new()
    {
        Id = "ord-1",
        Title = "Pole work",
        OrderTypeId = SharedUseOrderTypes.PoleWorkId,
        Status = ServiceOrderStatus.Completed,
        CreatedBy = "planner-1",
        OrganizationId = Guid.NewGuid(),
        CreatedAt = At.UtcDateTime,
        Attributes = sagaState is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { [OccupancyApplySagaAttributes.StateKey] = sagaState },
    };

    private static OrderType PoleWork()
    {
        var registry = new OrderTypeRegistry();
        SharedUseOrderTypes.RegisterAll(registry);
        return registry.Find(SharedUseOrderTypes.PoleWorkId)!;
    }
}
