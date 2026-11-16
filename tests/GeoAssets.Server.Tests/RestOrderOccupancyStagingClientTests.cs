using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <see cref="RestOrderOccupancyStagingService"/> against the real endpoints in a TestServer
/// (XD01-315): the same results, and — boundary-error parity — the same exception types the
/// in-process <see cref="OrderOccupancyStagingService"/> throws.
/// </summary>
public sealed class RestOrderOccupancyStagingClientTests
{
    private const string Target = "ord-1-t001";

    /// <summary>What the port requires a caller to pass; the wire never carries it.</summary>
    private static readonly WorkflowPrincipal Claimed =
        new("claimed-admin", OrderTargetsApiHost.OrgA.ToString(), ["Administrator"], [], ["serviceorders:annotate"]);

    private static async Task<(OrderTargetsApiHost Host, HttpClient Http, RestOrderOccupancyStagingService Sut)> StartAsync(
        string token = "admin", string status = ServiceOrderStatus.Draft)
    {
        var host = await OrderTargetsApiHost.StartAsync(mapOccupancyRoutes: true);
        await host.SeedOrderAsync(status: status);
        await host.SeedTargetsAsync("ord-1", 3);
        var entries = Enumerable.Range(1, 6)
            .Select(slot => new PoleOccupancyEntryBuilder().ForPole("f-001").OwnedBy(OrderTargetsApiHost.OrgA).OnSlot(slot).Build())
            .ToList();
        await host.Occupancy.ForOrganization(OrderTargetsApiHost.OrgA).ReplaceEntriesAsync("f-001", 0, entries, CancellationToken.None);
        var http = host.ClientFor(token);
        return (host, http, new RestOrderOccupancyStagingService(http));
    }

    [Fact]
    public async Task StageThenGet_RoundTripTheViewTheHostComputed_EntriesChangesAndPreviewIncluded()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;
        var live = host.Occupancy.Snapshot().Single();
        OccupancyChange[] changes =
        [
            OccupancyChangeBuilder.AddBeyondRegulatedSlots() with { AnatelLicenseNumber = "SCM-9", AttachedCableFeatureId = "cable-9", CableLoadDaN = 0.125m },
            OccupancyChangeBuilder.MarkSlotUnidentified(2),
            OccupancyChangeBuilder.ModifySlot(3, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
            OccupancyChangeBuilder.RemoveSlot(6),
        ];

        var staged = await sut.StageAsync("ord-1", Target, changes, Claimed, CancellationToken.None);
        var read = await sut.GetAsync("ord-1", Target, Claimed, CancellationToken.None);

        read.Should().BeEquivalentTo(staged);
        staged.Should().BeEquivalentTo(new
        {
            OrderId = "ord-1", TargetId = Target, PoleFeatureId = "f-001", PoleOwnerOrganizationId = OrderTargetsApiHost.OrgA,
            CurrentVersion = 1, BaselineVersion = (int?)1, IsBaselineStale = false,
        });
        staged.Current.Entries.Should().BeEquivalentTo(live.Entries, options => options.WithStrictOrdering());
        staged.Staged!.Changes.Should().Equal(changes);
        staged.Staged.StagedBy.Should().Be(OrderTargetsApiHost.Identities["admin"].UserId.ToString(), "the caller is the token's, not the principal the client passed");
        staged.Staged.StagedAt.Should().Be(OrderTargetsApiHost.Now);
        staged.Preview.Should().BeEquivalentTo(OccupancyChangeApplier.Apply(live, "f-001", OrderTargetsApiHost.OrgA, changes));
        staged.Preview.Should().BeEquivalentTo(new { OccupiedCount = 6, IsOverCapacity = false, HasUnidentifiedOccupant = true, Class = OccupancyClass.Unidentified });
    }

    [Fact]
    public async Task GetPageAsync_DiscardAsync_AndDiscardAllAsync_WorkOverTheRealRoutes()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;
        foreach (var target in new[] { "ord-1-t003", "ord-1-t002", Target })
            await sut.StageAsync("ord-1", target, [OccupancyChangeBuilder.AddBeyondRegulatedSlots()], Claimed, CancellationToken.None);

        var firstPage = await sut.GetPageAsync("ord-1", 1, 2, OccupancyChangeSetStatus.Staged, Claimed, CancellationToken.None);
        var discardedOne = await sut.DiscardAsync("ord-1", "ord-1-t002", Claimed, CancellationToken.None);
        var again = await sut.DiscardAsync("ord-1", "ord-1-t002", Claimed, CancellationToken.None);
        var discardedRest = await sut.DiscardAllAsync("ord-1", Claimed, CancellationToken.None);
        var all = await sut.GetPageAsync("ord-1", 1, 50, null, Claimed, CancellationToken.None);
        var stillStaged = await sut.GetPageAsync("ord-1", 1, 50, OccupancyChangeSetStatus.Staged, Claimed, CancellationToken.None);

        firstPage.TotalCount.Should().Be(3);
        firstPage.Items.Select(set => set.PoleFeatureId).Should().Equal("f-001", "f-002");
        discardedOne.Should().BeTrue();
        again.Should().BeFalse();
        discardedRest.Should().Be(2);
        all.TotalCount.Should().Be(3);
        all.Items.Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Discarded && set.ClosedAt == OrderTargetsApiHost.Now);
        all.Items.Should().BeEquivalentTo(host.ChangeSets.Snapshot().Select(OccupancyChangeSetView.From));
        stillStaged.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Failures_ComeBackAsTheExceptionsTheInProcessServiceThrows()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;
        await host.Store.AddRangeAsync(
            "ord-1",
            [new ServiceOrderTargetBuilder().WithId("ord-1-mh").WithOrderId("ord-1").WithFeatureId("mh-1").WithAssetTypeId("manhole").WithOrganizationId(OrderTargetsApiHost.OrgA).Build()],
            CancellationToken.None);
        host.ChangeSets.MarkReleased("ord-1-t003");
        OccupancyChange[] ok = [OccupancyChangeBuilder.AddBeyondRegulatedSlots()];

        await ((Func<Task>)(() => sut.GetAsync("ord-0", Target, Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<KeyNotFoundException>();
        await ((Func<Task>)(() => sut.GetAsync("ord-1", "t-missing", Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<OrderTargetNotFoundException>();
        await ((Func<Task>)(() => sut.GetAsync("ord-1", "ord-1-mh", Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<TargetIsNotAPoleException>();
        await ((Func<Task>)(() => sut.StageAsync("ord-1", Target, [], Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<InvalidOccupancyChangeException>();
        await ((Func<Task>)(() => sut.StageAsync("ord-1", Target, [OccupancyChangeBuilder.Add(1)], Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<InvalidOccupancyChangeException>();
        await ((Func<Task>)(() => sut.StageAsync("ord-1", Target, [OccupancyChangeBuilder.Add(9)], Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<InvalidOccupancyChangeException>();
        await ((Func<Task>)(() => sut.StageAsync("ord-1", "ord-1-t002", [OccupancyChangeBuilder.RemoveSlot(4)], Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<OccupancyEntryNotFoundException>();
        await ((Func<Task>)(() => sut.StageAsync("ord-1", "ord-1-t003", ok, Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<OccupancyTargetReleasedException>();
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ATerminalOrder_AnOutsider_AGrantee_AndAMemberWithoutARole_GetTheInProcessExceptions()
    {
        var (closedHost, closedHttp, closed) = await StartAsync(status: ServiceOrderStatus.Completed);
        await using var _ = closedHost;
        using var __ = closedHttp;
        var (host, outsiderHttp, outsider) = await StartAsync(token: "admin-b");
        await using var ___ = host;
        using var ____ = outsiderHttp;
        using var readerHttp = host.ClientFor("reader");
        var reader = new RestOrderOccupancyStagingService(readerHttp);
        OccupancyChange[] ok = [OccupancyChangeBuilder.AddBeyondRegulatedSlots()];

        (await ((Func<Task>)(() => closed.StageAsync("ord-1", Target, ok, Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<InvalidServiceOrderTransitionException>())
            .Which.From.Should().Be(ServiceOrderStatus.Completed);
        await ((Func<Task>)(() => outsider.GetAsync("ord-1", Target, Claimed, CancellationToken.None)))
            .Should().ThrowExactlyAsync<KeyNotFoundException>("outside the organization gate an order does not exist");
        (await ((Func<Task>)(() => reader.StageAsync("ord-1", Target, ok, Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<OrderActionDeniedException>())
            .Which.Action.Should().Be(OrderActionType.Annotate);

        host.Grants.Active.Add(new()
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            ResourceType = "ServiceOrder",
            AllowedActions = { "serviceorders:view", "serviceorders:annotate" },
        });
        (await ((Func<Task>)(() => outsider.GetAsync("ord-1", Target, Claimed, CancellationToken.None))).Should().ThrowExactlyAsync<OrderActionDeniedException>())
            .Which.Evaluation!.Reason.Should().Contain("organization that owns");
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }
}
