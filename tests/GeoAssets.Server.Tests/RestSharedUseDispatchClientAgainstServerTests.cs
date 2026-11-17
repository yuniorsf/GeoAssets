using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rules;
using Xunit;
using static GeoAssets.Server.Tests.SharedUseApiSeed;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <see cref="RestSharedUseDispatchClient"/> against the real routes in a TestServer (XD01-316):
/// the same results as the in-process <see cref="SharedUseDispatchService"/>, and — boundary-error
/// parity — the same exception types for the same failures.
/// </summary>
public sealed class RestSharedUseDispatchClientAgainstServerTests
{
    /// <summary>What the ports require a caller to pass; the wire never carries it.</summary>
    private static readonly WorkflowPrincipal Claimed =
        new("claimed", OrderTargetsApiHost.OrgA.ToString(), ["Administrator"], [], []);

    private static async Task<(OrderTargetsApiHost Host, HttpClient Http, RestSharedUseDispatchClient Sut)> ClientAsync(
        string token = "admin", string status = ServiceOrderStatus.Draft, int poles = 2, bool occupied = true)
    {
        var host = await StartAsync(status, poles, occupied);
        var http = host.ClientFor(token);
        return (host, http, new RestSharedUseDispatchClient(http));
    }

    [Fact]
    public async Task PreviewThenDispatch_RoundTripWhatTheHostComputed()
    {
        var (host, http, sut) = await ClientAsync();
        await using var _ = host;
        using var __ = http;

        var preview = await sut.PreviewAsync(Order, 1, 50, Claimed, CancellationToken.None);
        var result = await sut.DispatchAsync(Order, preview.RowVersion, Claimed, CancellationToken.None);
        var after = await sut.PreviewAsync(Order, 1, 1, Claimed, CancellationToken.None);

        preview.Should().BeEquivalentTo(new
        {
            OrderId = Order,
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            ChildOrderTypeId = SharedUseOrderTypes.OccupantNoticeId,
            StatusAfterDispatch = ServiceOrderStatus.Pending,
            TargetCount = 2,
            NewRecipientCount = 2,
            CanDispatch = true,
            BlockReason = (SharedUseDispatchBlockReason?)null,
        });
        preview.RowVersion.Should().BeEmpty();
        preview.Recipients.Items.Should().Equal(
            new SharedUseDispatchRecipient(OrderTargetsApiHost.OrgB, ChildOf(OrderTargetsApiHost.OrgB), 1, false),
            new SharedUseDispatchRecipient(OrderTargetsApiHost.OrgC, ChildOf(OrderTargetsApiHost.OrgC), 2, false));
        result.Should().BeEquivalentTo(new SharedUseDispatchResult
        {
            ParentOrderId = Order,
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            ParentStatus = ServiceOrderStatus.Pending,
            ChildOrdersCreated = 2,
            ChildOrdersAlreadyDispatched = 0,
            NotificationsEnqueued = 2,
            TargetCount = 2,
            BlockedTargetCount = 0,
            UnnotifiablePoleCount = 0,
            SkippedEntryCount = 0,
        });
        after.Recipients.TotalCount.Should().Be(2);
        after.Recipients.Items.Should().ContainSingle().Which.AlreadyDispatched.Should().BeTrue();
        after.CanDispatch.Should().BeFalse();
    }

    [Fact]
    public async Task SkippedEntriesAndUnnotifiablePoles_ComeBackAsTheServiceReportsThem()
    {
        var (host, http, sut) = await ClientAsync(poles: 2, occupied: false);
        await using var _ = host;
        using var __ = http;
        var abandoned = Entry(Pole(1), slot: null, OrderTargetsApiHost.OrgB, PoleAttachmentOccupantStatus.AbandonedDeadDrop);
        await OccupyAsync(host, Pole(1), Entry(Pole(1), 1, OrderTargetsApiHost.OrgC), abandoned);

        var skipped = await sut.GetSkippedEntriesAsync(Order, 1, 50, Claimed, CancellationToken.None);
        var poles = await sut.GetUnnotifiablePolesAsync(Order, 1, 50, Claimed, CancellationToken.None);
        var beyond = await sut.GetUnnotifiablePolesAsync(Order, 2, 50, Claimed, CancellationToken.None);

        skipped.TotalCount.Should().Be(1);
        skipped.Items.Should().Equal(
            new SharedUseSkippedEntry(Pole(1), abandoned.Id, null, PoleAttachmentOccupantStatus.AbandonedDeadDrop, SharedUseSkipReason.NoActiveContract));
        poles.Items.Should().Equal(Pole(2));
        poles.TotalCount.Should().Be(1);
        beyond.Items.Should().BeEmpty();
        beyond.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task UnknownOrder_ThrowsKeyNotFoundException_AsInProcess()
    {
        var (host, http, sut) = await ClientAsync();
        await using var _ = host;
        using var __ = http;

        await ((Func<Task>)(() => sut.DispatchAsync("ord-missing", [], Claimed, CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
        await ((Func<Task>)(() => sut.PreviewAsync("ord-missing", 1, 50, Claimed, CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CallerOutsideTheOrganization_GetsKeyNotFound_TheSameAsAnUnknownOrder()
    {
        var (host, http, sut) = await ClientAsync(token: "admin-b");
        await using var _ = host;
        using var __ = http;

        await ((Func<Task>)(() => sut.DispatchAsync(Order, [], Claimed, CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task MemberWithoutTheRightToDispatch_ThrowsOrderActionDenied_ForDispatch_WithTheHostsReason()
    {
        var (host, http, sut) = await ClientAsync(token: "reader");
        await using var _ = host;
        using var __ = http;

        var act = () => sut.DispatchAsync(Order, [], Claimed, CancellationToken.None);

        var denied = (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Action.Should().Be(OrderActionType.Dispatch);
        denied.OrderId.Should().Be(Order);
        denied.Evaluation!.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OrderTypeProblems_ComeBackTyped_NamingTheOrderType()
    {
        var (host, http, sut) = await ClientAsync();
        await using var _ = host;
        using var __ = http;
        await host.SeedOrderAsync("ord-ghost", "ghost-type");
        await host.SeedOrderAsync("ord-survey", OrderTargetsApiHost.PoleSurvey);

        var ghost = () => sut.DispatchAsync("ord-ghost", [], Claimed, CancellationToken.None);
        var survey = () => sut.PreviewAsync("ord-survey", 1, 50, Claimed, CancellationToken.None);

        (await ghost.Should().ThrowAsync<OrderTypeNotRegisteredException>()).Which.OrderTypeId.Should().Be("ghost-type");
        (await survey.Should().ThrowAsync<OrderTypeHasNoFanOutException>()).Which.OrderTypeId.Should().Be(OrderTargetsApiHost.PoleSurvey);
    }

    [Fact]
    public async Task BlockedDispatch_ComesBackAsSharedUseDispatchBlocked_WithItsReasonAndTargets()
    {
        var (host, http, sut) = await ClientAsync();
        await using var _ = host;
        using var __ = http;
        await SeedTargetsAsync(host, Order, [], Pole(9));

        var act = () => sut.DispatchAsync(Order, [], Claimed, CancellationToken.None);

        var blocked = (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>()).Which;
        blocked.Reason.Should().Be(SharedUseDispatchBlockReason.TargetsWithoutActivity);
        blocked.OrderId.Should().Be(Order);
        blocked.TargetIds.Should().Equal($"{Order}-{Pole(9)}");
        blocked.TargetCount.Should().Be(1);
    }

    [Theory]
    [InlineData(0, false, SharedUseDispatchBlockReason.NoTargets)]
    [InlineData(2, false, SharedUseDispatchBlockReason.NoEligibleOccupants)]
    public async Task BlockedDispatch_OtherReasons_ComeBackWithTheSameReason(int poles, bool occupied, SharedUseDispatchBlockReason expected)
    {
        var (host, http, sut) = await ClientAsync(poles: poles, occupied: occupied);
        await using var _ = host;
        using var __ = http;

        var act = () => sut.DispatchAsync(Order, [], Claimed, CancellationToken.None);

        var blocked = (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>()).Which;
        blocked.Reason.Should().Be(expected);
        blocked.TargetIds.Should().BeEmpty();
    }

    [Fact]
    public async Task OrderInAStatusWithNoDispatch_ThrowsInvalidServiceOrderTransitionException_WithTheStatus()
    {
        var (host, http, sut) = await ClientAsync(status: ServiceOrderStatus.Completed);
        await using var _ = host;
        using var __ = http;

        var act = () => sut.DispatchAsync(Order, [], Claimed, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which.From.Should().Be(ServiceOrderStatus.Completed);
    }
}
