using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

/// <summary>The approval branch of the generic action route, as the NeutralOperator module's order action handler (XD01-360).</summary>
public sealed class OccupancyApprovalHandlerTests
{
    private const string Open = "open";
    private const string Approved = "approved";

    private static readonly OrderType Targeted = new()
    {
        Id = "targeted",
        DisplayName = "Targeted",
        TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] },
        States = [new WorkflowState(Open, "Open"), new WorkflowState(Approved, "Approved", IsSuccess: true)],
        Transitions = [new WorkflowTransition(Open, Approved, OrderActionType.Approve)],
    };

    private static readonly OrderType Untargeted = new() { Id = "untargeted", DisplayName = "Untargeted" };

    private static readonly WorkflowPrincipal Principal = new("user-1", null, [], [], []);

    private sealed class StubCommitService(Func<Exception?> toThrow) : IOccupancyCommitService
    {
        public List<(string OrderId, WorkflowPrincipal Principal)> Calls { get; } = [];

        public Task<OccupancyCommitResult> ApproveAndCommitAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
        {
            Calls.Add((orderId, principal));
            if (toThrow() is { } exception) throw exception;
            return Task.FromResult<OccupancyCommitResult>(null!);
        }
    }

    private static OrderActionLog Entry(OrderActionType action, string? resultingStatus = null) =>
        new(action, "whoever", DateTime.UnixEpoch, ResultingStatus: resultingStatus);

    private static OrderActionContext Context(OrderActionLog entry) =>
        new(new ServiceOrder { Id = "ord-1", OrderTypeId = Targeted.Id, Status = Open }, Targeted, entry, Principal);

    private static OccupancyApprovalHandler HandlerThrowing(Exception? exception, out StubCommitService commits)
    {
        commits = new StubCommitService(() => exception);
        return new OccupancyApprovalHandler(commits);
    }

    [Fact]
    public void CanHandle_ApproveOnTargetedType_True()
    {
        var handler = HandlerThrowing(null, out _);

        handler.CanHandle(Targeted, Entry(OrderActionType.Approve)).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_OtherActionsOrUntargetedType_False()
    {
        var handler = HandlerThrowing(null, out _);

        handler.CanHandle(Targeted, Entry(OrderActionType.Reject)).Should().BeFalse();
        handler.CanHandle(Untargeted, Entry(OrderActionType.Approve)).Should().BeFalse();
        handler.CanHandle(null, Entry(OrderActionType.Approve)).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_NullEntry_Throws()
    {
        var handler = HandlerThrowing(null, out _);

        var act = () => handler.CanHandle(Targeted, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public Task HandleAsync_NullContext_Throws()
    {
        var handler = HandlerThrowing(null, out _);

        var act = () => handler.HandleAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task HandleAsync_RequestedStatusNotWhereApprovalLeads_IsInvalidTransition()
    {
        var handler = HandlerThrowing(null, out var commits);

        var outcome = await handler.HandleAsync(Context(Entry(OrderActionType.Approve, "somewhere-else")), CancellationToken.None);

        outcome.Kind.Should().Be(OrderActionOutcomeKind.InvalidTransition);
        outcome.From.Should().Be(Open);
        outcome.To.Should().Be("somewhere-else");
        commits.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Approved)]
    public async Task HandleAsync_Commits_IsApplied(string? resultingStatus)
    {
        var handler = HandlerThrowing(null, out var commits);

        var outcome = await handler.HandleAsync(Context(Entry(OrderActionType.Approve, resultingStatus)), CancellationToken.None);

        outcome.Should().BeSameAs(OrderActionOutcome.Applied);
        commits.Calls.Should().ContainSingle().Which.Should().Be(("ord-1", Principal));
    }

    public static TheoryData<Exception, OrderActionOutcomeKind> ThrownOutcomes => new()
    {
        { new OrderActionDeniedException("ord-1", RuleEvaluationResult.Deny(OrderActionType.Approve, "no")), OrderActionOutcomeKind.Denied },
        { new KeyNotFoundException(), OrderActionOutcomeKind.NotFound },
        { new InvalidServiceOrderTransitionException(Open, "x"), OrderActionOutcomeKind.InvalidTransition },
        { new OccupancyConflictException(["p1", "p2"]), OrderActionOutcomeKind.Problem },
        { new ServiceOrderConcurrencyException("ord-1"), OrderActionOutcomeKind.ConcurrentModification },
    };

    [Theory]
    [MemberData(nameof(ThrownOutcomes))]
    public async Task HandleAsync_ClientFacingFailure_BecomesAnOutcome(Exception thrown, OrderActionOutcomeKind kind)
    {
        var handler = HandlerThrowing(thrown, out _);

        var outcome = await handler.HandleAsync(Context(Entry(OrderActionType.Approve)), CancellationToken.None);

        outcome.Kind.Should().Be(kind);
    }

    [Fact]
    public async Task HandleAsync_DeniedFailure_CarriesTheVerdict()
    {
        var verdict = RuleEvaluationResult.Deny(OrderActionType.Approve, "no");
        var handler = HandlerThrowing(new OrderActionDeniedException("ord-1", verdict), out _);

        var outcome = await handler.HandleAsync(Context(Entry(OrderActionType.Approve)), CancellationToken.None);

        outcome.Verdict.Should().BeSameAs(verdict);
    }

    [Fact]
    public async Task HandleAsync_InvalidTransitionFailure_CarriesFromAndTo()
    {
        var handler = HandlerThrowing(new InvalidServiceOrderTransitionException("a", "b"), out _);

        var outcome = await handler.HandleAsync(Context(Entry(OrderActionType.Approve)), CancellationToken.None);

        outcome.From.Should().Be("a");
        outcome.To.Should().Be("b");
    }

    [Fact]
    public async Task HandleAsync_OccupancyConflict_IsProblem409WithCodeAndPoleFeatureIds()
    {
        var handler = HandlerThrowing(new OccupancyConflictException(["p1", "p2"]), out _);

        var outcome = await handler.HandleAsync(Context(Entry(OrderActionType.Approve)), CancellationToken.None);

        outcome.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        outcome.Code.Should().Be(OccupancyProblemCodes.ConcurrentModification);
        outcome.Extensions.Should().ContainSingle().Which.Name.Should().Be("poleFeatureIds");
        outcome.Extensions[0].Value.Should().BeEquivalentTo(new[] { "p1", "p2" });
    }

    [Fact]
    public async Task HandleAsync_UnexpectedFailure_Propagates()
    {
        var handler = HandlerThrowing(new InvalidOperationException("boom"), out _);

        var act = () => handler.HandleAsync(Context(Entry(OrderActionType.Approve)), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage("boom");
    }
}
