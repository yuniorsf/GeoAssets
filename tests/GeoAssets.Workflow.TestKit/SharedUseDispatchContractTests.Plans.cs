using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// <see cref="SharedUseDispatchContractTests"/>: the plan reads, what is skipped and reported,
/// the orders that cannot be dispatched, and the callers that may not.
/// </summary>
public abstract partial class SharedUseDispatchContractTests
{
    // ── Skipped entries and poles with nobody to notify ───────────────────────

    [Fact]
    public async Task DispatchAsync_SkipsEveryEntryWithoutAnActiveContractOrAKnownOrganization_AndReportsEachWithItsReason()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1", "pole-2", "pole-3");
        var clandestine = Entry(harness, "pole-1", 2, OccupantTwo, PoleAttachmentOccupantStatus.ClandestineUnauthorized);
        var unidentified = Entry(harness, "pole-1", 3, occupant: null);
        var ownersOwn = Entry(harness, "pole-1", 4, harness.OwnerOrganizationId);
        var reservation = Entry(harness, "pole-2", 1, OccupantThree, PoleAttachmentOccupantStatus.ActiveReservation);
        var abandonedBeyondSlots = Entry(harness, "pole-2", slot: null, OccupantThree, PoleAttachmentOccupantStatus.AbandonedDeadDrop);
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne), clandestine, unidentified, ownersOwn);
        await OccupyAsync(harness, "pole-2", reservation, abandonedBeyondSlots);

        var result = await DispatchAsync(harness, "ord-1");

        result.ChildOrdersCreated.Should().Be(1, "only the contracted, identified occupant is dispatched to");
        result.SkippedEntryCount.Should().Be(5);
        result.UnnotifiablePoleCount.Should().Be(2, "pole-2 has nobody under contract and pole-3 has no occupancy on file");
        (await harness.Orders.GetChildrenAsync("ord-1")).Select(c => c.Id)
            .Should().Equal(SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne));

        var skipped = await harness.Plans.GetSkippedEntriesAsync("ord-1", 1, 50, Dispatcher(harness), CancellationToken.None);
        skipped.TotalCount.Should().Be(5);
        skipped.Items.Should().BeEquivalentTo(
        [
            new SharedUseSkippedEntry("pole-1", clandestine.Id, 2, PoleAttachmentOccupantStatus.ClandestineUnauthorized, SharedUseSkipReason.NoActiveContract),
            new SharedUseSkippedEntry("pole-1", unidentified.Id, 3, PoleAttachmentOccupantStatus.AuthorizedContracted, SharedUseSkipReason.UnknownOrganization),
            new SharedUseSkippedEntry("pole-1", ownersOwn.Id, 4, PoleAttachmentOccupantStatus.AuthorizedContracted, SharedUseSkipReason.PoleOwner),
            new SharedUseSkippedEntry("pole-2", reservation.Id, 1, PoleAttachmentOccupantStatus.ActiveReservation, SharedUseSkipReason.NoActiveContract),
            new SharedUseSkippedEntry("pole-2", abandonedBeyondSlots.Id, null, PoleAttachmentOccupantStatus.AbandonedDeadDrop, SharedUseSkipReason.NoActiveContract),
        ]);
        skipped.Items.Select(e => e.PoleFeatureId).Should().BeInAscendingOrder(StringComparer.Ordinal);

        var unnotifiable = await harness.Plans.GetUnnotifiablePolesAsync("ord-1", 1, 50, Dispatcher(harness), CancellationToken.None);
        unnotifiable.Items.Should().Equal("pole-2", "pole-3");
        unnotifiable.TotalCount.Should().Be(2);
    }

    // ── Plan reads ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_SaysWhatADispatchWouldDo_AndWritesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantTwo), Entry(harness, "pole-1", 2, OccupantOne));
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantOne));

        var preview = await harness.Plans.PreviewAsync("ord-1", 1, 50, Dispatcher(harness), CancellationToken.None);

        preview.OrderId.Should().Be("ord-1");
        preview.OrderTypeId.Should().Be(SharedUseOrderTypes.PoleWorkId);
        preview.ChildOrderTypeId.Should().Be(SharedUseOrderTypes.OccupantNoticeId);
        preview.StatusAfterDispatch.Should().Be(ServiceOrderStatus.Pending);
        preview.TargetCount.Should().Be(2);
        preview.NewRecipientCount.Should().Be(2);
        preview.CanDispatch.Should().BeTrue();
        preview.BlockReason.Should().BeNull();
        preview.Recipients.TotalCount.Should().Be(2);
        preview.Recipients.Items.Should().Equal(
            new SharedUseDispatchRecipient(OccupantOne, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne), 2, false),
            new SharedUseDispatchRecipient(OccupantTwo, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantTwo), 1, false));

        (await harness.Orders.GetChildrenAsync("ord-1")).Should().BeEmpty();
        (await harness.StagedNotificationsAsync(CancellationToken.None)).Should().BeEmpty();
        var parent = (await harness.Orders.GetByIdAsync("ord-1"))!;
        parent.Status.Should().Be(ServiceOrderStatus.Draft);
        parent.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_PagesItsRecipients_InOrganizationOrder_WithTheTrueTotal()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(
            harness, "pole-1",
            Entry(harness, "pole-1", 1, OccupantThree), Entry(harness, "pole-1", 2, OccupantOne), Entry(harness, "pole-1", 3, OccupantTwo));

        var first = await harness.Plans.PreviewAsync("ord-1", 1, 2, Dispatcher(harness), CancellationToken.None);
        var second = await harness.Plans.PreviewAsync("ord-1", 2, 2, Dispatcher(harness), CancellationToken.None);
        var beyond = await harness.Plans.PreviewAsync("ord-1", 3, 2, Dispatcher(harness), CancellationToken.None);

        first.Recipients.Items.Select(r => r.OccupantOrganizationId).Should().Equal(OccupantOne, OccupantTwo);
        second.Recipients.Items.Select(r => r.OccupantOrganizationId).Should().Equal(OccupantThree);
        beyond.Recipients.Items.Should().BeEmpty();
        new[] { first, second, beyond }.Should().OnlyContain(p => p.Recipients.TotalCount == 3 && p.NewRecipientCount == 3);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public async Task PlanReads_PagingOutOfRange_ThrowArgumentOutOfRangeException(int page, int pageSize)
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        var principal = Dispatcher(harness);

        var preview = () => harness.Plans.PreviewAsync("ord-1", page, pageSize, principal, CancellationToken.None);
        var skipped = () => harness.Plans.GetSkippedEntriesAsync("ord-1", page, pageSize, principal, CancellationToken.None);
        var poles = () => harness.Plans.GetUnnotifiablePolesAsync("ord-1", page, pageSize, principal, CancellationToken.None);

        await preview.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await skipped.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await poles.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ── Orders that cannot be dispatched as they stand ────────────────────────

    [Fact]
    public async Task DispatchAsync_OrderWithNoTargets_IsBlocked_AndThePreviewSaysWhy()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft);

        var act = () => DispatchAsync(harness, "ord-1");

        (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>()).Which.Reason.Should().Be(SharedUseDispatchBlockReason.NoTargets);
        var preview = await harness.Plans.PreviewAsync("ord-1", 1, 50, Dispatcher(harness), CancellationToken.None);
        preview.BlockReason.Should().Be(SharedUseDispatchBlockReason.NoTargets);
        preview.CanDispatch.Should().BeFalse();
        await AssertNothingWasDispatchedAsync(harness, "ord-1");
    }

    [Fact]
    public async Task DispatchAsync_ATargetWithoutAnActivity_IsBlocked_NamingTheTarget_AndNothingIsWritten()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await AddTargetsAsync(harness, "ord-1", [], "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantOne));

        var act = () => DispatchAsync(harness, "ord-1");

        var blocked = (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>()).Which;
        blocked.Reason.Should().Be(SharedUseDispatchBlockReason.TargetsWithoutActivity);
        blocked.TargetIds.Should().Equal(OrderTargetSelectionService.TargetId("ord-1", "pole-2"));
        blocked.TargetCount.Should().Be(1);
        (await harness.Plans.PreviewAsync("ord-1", 1, 50, Dispatcher(harness), CancellationToken.None))
            .TargetsWithoutActivityCount.Should().Be(1);
        await AssertNothingWasDispatchedAsync(harness, "ord-1");
    }

    [Fact]
    public async Task DispatchAsync_NoOccupantUnderContract_IsBlocked_AndNothingIsWritten()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne, PoleAttachmentOccupantStatus.ClandestineUnauthorized));

        var act = () => DispatchAsync(harness, "ord-1");

        (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>())
            .Which.Reason.Should().Be(SharedUseDispatchBlockReason.NoEligibleOccupants);
        await AssertNothingWasDispatchedAsync(harness, "ord-1");
    }

    [Fact]
    public async Task DispatchAsync_EveryTargetBlockedByAnotherOrder_IsBlocked()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await harness.Orders.AddAsync(new ServiceOrder
        {
            Id = "ord-exclusive",
            Title = "Exclusive",
            OrderTypeId = ExclusiveTypeId,
            CreatedBy = "planner-1",
            OrganizationId = harness.OwnerOrganizationId,
            CreatedAt = CreatedAt,
        });
        await AddTargetsAsync(harness, "ord-exclusive", [], "pole-1");

        var act = () => DispatchAsync(harness, "ord-1");

        (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>())
            .Which.Reason.Should().Be(SharedUseDispatchBlockReason.AllTargetsBlocked);
        await AssertNothingWasDispatchedAsync(harness, "ord-1");
    }

    [Theory]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public async Task DispatchAsync_FromAStatusWithNoDispatchTransition_ThrowsInvalidServiceOrderTransitionException(string status)
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", status, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));

        var act = () => DispatchAsync(harness, "ord-1");

        var refused = (await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which;
        refused.From.Should().Be(status);
        (await harness.Orders.GetChildrenAsync("ord-1")).Should().BeEmpty();
    }

    // ── Callers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_UnknownOrder_ThrowsKeyNotFoundException()
    {
        await using var harness = await CreateHarnessAsync();
        RegisterTypes(harness);

        var act = () => harness.Service.DispatchAsync("ord-missing", [], Dispatcher(harness), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DispatchAndPlanReads_ByAMemberWithoutTheRightToDispatch_AreDenied_AndNothingIsWritten()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        var member = new WorkflowPrincipal("crew-1", harness.OwnerOrganizationId.ToString(), [], [], []);

        var dispatch = () => harness.Service.DispatchAsync("ord-1", [], member, CancellationToken.None);
        var preview = () => harness.Plans.PreviewAsync("ord-1", 1, 50, member, CancellationToken.None);

        (await dispatch.Should().ThrowAsync<OrderActionDeniedException>()).Which.Action.Should().Be(OrderAction.Dispatch);
        await preview.Should().ThrowAsync<OrderActionDeniedException>();
        await AssertNothingWasDispatchedAsync(harness, "ord-1");
    }

    [Fact]
    public async Task DispatchAsync_ByAnAdministratorOfAnotherOrganization_IsRefusedAsOutsideTheOrganization()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        var outsider = new WorkflowPrincipal("admin-x", OccupantOne.ToString(), ["Administrator"], [], []);

        var act = () => harness.Service.DispatchAsync("ord-1", [], outsider, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.IsOutsideOrganization.Should().BeTrue();
        await AssertNothingWasDispatchedAsync(harness, "ord-1");
    }

    [Fact]
    public async Task DispatchAsync_TokenAlreadyCancelled_ThrowsOperationCanceledException_AndWritesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => harness.Service.DispatchAsync("ord-1", [], Dispatcher(harness), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertNothingWasDispatchedAsync(harness, "ord-1");
    }

    /// <summary>The order is as it was seeded: no child, no notification, still a draft with an empty log.</summary>
    protected static async Task AssertNothingWasDispatchedAsync(SharedUseDispatchContractHarness harness, string orderId)
    {
        ArgumentNullException.ThrowIfNull(harness);

        (await harness.Orders.GetChildrenAsync(orderId)).Should().BeEmpty();
        (await harness.StagedNotificationsAsync(CancellationToken.None)).Should().BeEmpty();
        var parent = (await harness.Orders.GetByIdAsync(orderId))!;
        parent.Status.Should().Be(ServiceOrderStatus.Draft);
        parent.ActionLog.Should().BeEmpty();
        parent.Dispatches.Should().BeEmpty();
    }
}
