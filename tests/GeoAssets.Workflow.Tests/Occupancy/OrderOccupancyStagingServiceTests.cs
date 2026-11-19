using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Workflow.Tests.Occupancy.OccupancyStagingFixture;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary>
/// <see cref="OrderOccupancyStagingService"/> (XD01-315): who may read and stage, on what, and
/// what the target's view says — the pole now, what is staged, the preview and the stale notice.
/// </summary>
public sealed class OrderOccupancyStagingServiceTests
{
    private static readonly OccupancyChange[] OneAdd = [OccupancyChangeBuilder.Add(slotIndex: 4)];

    private delegate Task Call(OrderOccupancyStagingService sut, string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>Every member, as "run it for this order, target and principal"; the bool says whether it writes.</summary>
    private static readonly (string Name, bool Writes, bool AddressesATarget, Call Run)[] Members =
    [
        ("get", false, true, (sut, o, t, p, ct) => sut.GetAsync(o, t, p, ct)),
        ("page", false, false, (sut, o, _, p, ct) => sut.GetPageAsync(o, 1, 50, null, p, ct)),
        ("stage", true, true, (sut, o, t, p, ct) => sut.StageAsync(o, t, OneAdd, p, ct)),
        ("discard", true, true, (sut, o, t, p, ct) => sut.DiscardAsync(o, t, p, ct)),
        ("discard-all", true, false, (sut, o, _, p, ct) => sut.DiscardAllAsync(o, p, ct)),
    ];

    public static TheoryData<string> EveryMember => [.. Members.Select(m => m.Name)];

    public static TheoryData<string> TargetMembers => [.. Members.Where(m => m.AddressesATarget).Select(m => m.Name)];

    private static (string Name, bool Writes, bool AddressesATarget, Call Run) Member(string name) => Members.Single(m => string.Equals(m.Name, name, StringComparison.Ordinal));

    // ── Guards ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryMember))]
    public async Task NullPrincipal_ThrowsArgumentNullException_AndBlankOrderId_ThrowsArgumentException(string member)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var run = Member(member).Run;

        await ((Func<Task>)(() => run(fixture.Sut, OrderId, TargetId(), null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => run(fixture.Sut, " ", TargetId(), Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(TargetMembers))]
    public async Task BlankTargetId_ThrowsArgumentException(string member)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();

        var act = () => Member(member).Run(fixture.Sut, OrderId, " ", Admin, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task StageAsync_NullChanges_ThrowsArgumentNullException_AndPageOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();

        await ((Func<Task>)(() => fixture.Sut.StageAsync(OrderId, TargetId(), null!, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => fixture.Sut.GetPageAsync(OrderId, 0, 50, null, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await ((Func<Task>)(() => fixture.Sut.GetPageAsync(OrderId, 1, 201, null, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [MemberData(nameof(EveryMember))]
    public async Task UnknownOrder_ThrowsKeyNotFoundException_ThatIsNotATargetOrEntryOne(string member)
    {
        var fixture = new OccupancyStagingFixture();

        var act = () => Member(member).Run(fixture.Sut, "ord-missing", TargetId(), Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<KeyNotFoundException>()).Which.Should().BeOfType<KeyNotFoundException>();
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryMember))]
    public async Task PrincipalOutsideTheOrdersOrganization_IsDeniedWithTheOutsideOrganizationVerdict_BeforeAnyStoreIsTouched(string member)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var untouchable = new ThrowingStores();
        var sut = fixture.Build(untouchable, untouchable, untouchable, untouchable);

        var act = () => Member(member).Run(sut, OrderId, TargetId(), Outsider, CancellationToken.None);

        var denied = (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Evaluation!.IsOutsideOrganization.Should().BeTrue("a host answers this exactly as it answers an unknown order");
        denied.OrderId.Should().Be(OrderId);
    }

    [Theory]
    [MemberData(nameof(EveryMember))]
    public async Task MemberWithoutAnyRole_IsDeniedByTheRules_InsideTheGate(string member)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var (_, writes, _, run) = Member(member);

        var act = () => run(fixture.Sut, OrderId, TargetId(), Stranger, CancellationToken.None);

        var denied = (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Evaluation!.IsOutsideOrganization.Should().BeFalse();
        denied.Action.Should().Be(writes ? OrderAction.Annotate : OrderAction.View);
    }

    [Theory]
    [MemberData(nameof(EveryMember))]
    public async Task MemberWhoMayOnlyView_ReadsButNeitherStagesNorDiscards(string member)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var (_, writes, _, run) = Member(member);

        var act = () => run(fixture.Sut, OrderId, TargetId(), Viewer, CancellationToken.None);

        if (writes)
            (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which.Action.Should().Be(OrderAction.Annotate);
        else
            await act.Should().NotThrowAsync();
        fixture.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryMember))]
    public async Task CrossOrganizationGrantee_AllowedByTheRules_IsStillDenied_OccupancyIsThePoleOwnersOnly(string member)
    {
        var fixture = new OccupancyStagingFixture();
        var order = await fixture.SeedOrderAsync();
        var (_, writes, _, run) = Member(member);
        var action = writes ? OrderActionType.Annotate : OrderActionType.View;
        fixture.Rules.Evaluate(Grantee, action, order).Allowed.Should().BeTrue("the grant is what lets the grantee past the rules");
        var untouchable = new ThrowingStores();
        var sut = fixture.Build(untouchable, untouchable, untouchable, untouchable);

        var act = () => run(sut, OrderId, TargetId(), Grantee, CancellationToken.None);

        var denied = (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Evaluation!.IsOutsideOrganization.Should().BeFalse("the grantee can see the order; it is refused the occupancy, not the order");
        denied.Evaluation.Reason.Should().Be(OrderOccupancyStagingService.OwnerOnlyReason);
        denied.Action.Should().Be((OrderAction)action);
    }

    // ── The target ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(TargetMembers))]
    public async Task UnknownTarget_OrTheTargetOfAnotherOrder_ThrowsOrderTargetNotFound(string member)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        await fixture.SeedOrderAsync("ord-2", featureIds: "pole-2");
        var run = Member(member).Run;

        var unknown = () => run(fixture.Sut, OrderId, "tgt-missing", Admin, CancellationToken.None);
        var ofAnotherOrder = () => run(fixture.Sut, OrderId, TargetId("pole-2", "ord-2"), Admin, CancellationToken.None);

        (await unknown.Should().ThrowAsync<OrderTargetNotFoundException>()).Which.Should().BeEquivalentTo(new { OrderId, TargetId = "tgt-missing" });
        await ofAnotherOrder.Should().ThrowAsync<OrderTargetNotFoundException>();
    }

    [Theory]
    [MemberData(nameof(TargetMembers))]
    public async Task TargetThatIsNotAPole_ThrowsTargetIsNotAPole(string member)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync(assetTypeId: "manhole");

        var act = () => Member(member).Run(fixture.Sut, OrderId, TargetId(), Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<TargetIsNotAPoleException>()).Which.Should().BeEquivalentTo(new { TargetId = TargetId(), AssetTypeId = "manhole" });
        fixture.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task PoleAssetTypes_AreTheHostsToName_ComparedIgnoringCase_AndNoneConfiguredMeansNoTargetIsAPole()
    {
        var configured = new OccupancyStagingFixture("POLE", "utility-pole");
        await configured.SeedOrderAsync();
        var unconfigured = new OccupancyStagingFixture();
        unconfigured.Options.PoleAssetTypeIds.Clear();
        await unconfigured.SeedOrderAsync();

        (await configured.Sut.GetAsync(OrderId, TargetId(), Admin, CancellationToken.None)).PoleFeatureId.Should().Be(Pole);
        await ((Func<Task>)(() => unconfigured.Sut.GetAsync(OrderId, TargetId(), Admin, CancellationToken.None))).Should().ThrowAsync<TargetIsNotAPoleException>();
        new OrderOccupancyOptions().IsPole(null).Should().BeFalse();
    }

    // ── GetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_NothingOnFileAndNothingStaged_IsAnEmptyPoleAtVersionZero_PreviewingAsItself()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();

        var view = await fixture.Sut.GetAsync(OrderId, TargetId(), Admin, CancellationToken.None);

        view.Should().BeEquivalentTo(new TargetOccupancyView
        {
            OrderId = OrderId,
            TargetId = TargetId(),
            PoleFeatureId = Pole,
            PoleOwnerOrganizationId = OrgA,
            CurrentVersion = 0,
            Current = OccupancyProjection.Empty,
            Preview = OccupancyProjection.Empty,
        });
        view.Staged.Should().BeNull();
        view.BaselineVersion.Should().BeNull();
        view.IsBaselineStale.Should().BeFalse();
        view.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ShowsThePolesLiveOccupancy_OverCapacityEntriesIncluded()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var live = await fixture.SeedOccupancyAsync(regulated: 6, beyond: 3);

        var view = await fixture.Sut.GetAsync(OrderId, TargetId(), Viewer, CancellationToken.None);

        view.CurrentVersion.Should().Be(1);
        view.Current.Entries.Should().Equal(live.Entries);
        view.Current.Should().BeEquivalentTo(new { OccupiedCount = 9, IsOverCapacity = true, HasUnidentifiedOccupant = false, Class = OccupancyClass.OverCapacity });
        view.Preview.Should().BeEquivalentTo(view.Current);
    }

    // ── StageAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task StageAsync_StoresTheSetUnderThePoleOwner_WithThePolesVersionAsBaseline_AndReturnsTheViewWithThePreview()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var live = await fixture.SeedOccupancyAsync(regulated: 6);
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        var seventh = OccupancyChangeBuilder.AddBeyondRegulatedSlots();

        var view = await fixture.Sut.StageAsync(OrderId, TargetId(), [seventh, OccupancyChangeBuilder.MarkSlotUnidentified(2)], Admin, CancellationToken.None);

        var stored = fixture.ChangeSets.Snapshot().Should().ContainSingle().Subject;
        stored.Should().BeEquivalentTo(new
        {
            OrderId, TargetId = TargetId(), PoleFeatureId = Pole, PoleOwnerOrganizationId = OrgA, BaselineVersion = live.Version,
            Status = OccupancyChangeSetStatus.Staged, StagedBy = "admin-1", StagedAt = Now.AddMinutes(10),
        });
        view.Staged.Should().BeEquivalentTo(OccupancyChangeSetView.From(stored));
        view.BaselineVersion.Should().Be(1);
        view.IsBaselineStale.Should().BeFalse();
        view.Warnings.Should().BeEmpty();
        view.Current.OccupiedCount.Should().Be(6);
        view.Preview.Should().BeEquivalentTo(new { OccupiedCount = 7, IsOverCapacity = true, HasUnidentifiedOccupant = true, Class = OccupancyClass.Unidentified });
        view.Preview!.Entries.Should().HaveCount(7).And.Contain(entry => entry.Id == seventh.EntryId && entry.SlotIndex == null);
    }

    [Fact]
    public async Task StageAsync_NeverWritesThePolesLiveOccupancy()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var live = await fixture.SeedOccupancyAsync(regulated: 3, beyond: 1);

        await fixture.Sut.StageAsync(
            OrderId, TargetId(), [OccupancyChangeBuilder.RemoveSlot(1), OccupancyChangeBuilder.AddBeyondRegulatedSlots()], Admin, CancellationToken.None);
        await fixture.Sut.DiscardAsync(OrderId, TargetId(), Admin, CancellationToken.None);

        fixture.Occupancy.Snapshot().Should().ContainSingle().Which.Should().BeEquivalentTo(live);
    }

    [Fact]
    public async Task StageAsync_Again_ReplacesWhatWasStaged_AndIsSafeToRepeat()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var change = OccupancyChangeBuilder.Add(slotIndex: 4);
        await fixture.Sut.StageAsync(OrderId, TargetId(), [OccupancyChangeBuilder.Add(slotIndex: 1)], Admin, CancellationToken.None);

        var first = await fixture.Sut.StageAsync(OrderId, TargetId(), [change], Admin, CancellationToken.None);
        var second = await fixture.Sut.StageAsync(OrderId, TargetId(), [change], Admin, CancellationToken.None);

        fixture.ChangeSets.Snapshot().Should().ContainSingle().Which.Changes.Should().Equal(change);
        second.Preview.Should().BeEquivalentTo(first.Preview);
        second.Staged!.Changes.Should().Equal(first.Staged!.Changes);
    }

    [Fact]
    public async Task StageAsync_AgainstAPoleThatMovedSinceTheLastStaging_IsNotAnError_ItRecordsTheNewBaseline()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        await fixture.SeedOccupancyAsync(regulated: 2);
        await fixture.Sut.StageAsync(OrderId, TargetId(), OneAdd, Admin, CancellationToken.None);
        await fixture.SeedOccupancyAsync(regulated: 3);

        var stale = await fixture.Sut.GetAsync(OrderId, TargetId(), Admin, CancellationToken.None);
        var restaged = await fixture.Sut.StageAsync(OrderId, TargetId(), OneAdd, Admin, CancellationToken.None);

        stale.Should().BeEquivalentTo(new { CurrentVersion = 2, BaselineVersion = (int?)1, IsBaselineStale = true });
        stale.Warnings.Should().Equal(OccupancyProblemCodes.StaleBaseline);
        stale.Preview!.OccupiedCount.Should().Be(4, "the preview applies the staged changes to the pole as it is now");
        restaged.Should().BeEquivalentTo(new { CurrentVersion = 2, BaselineVersion = (int?)2, IsBaselineStale = false });
        restaged.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_StagedChangesThatNoLongerApply_HaveNoPreview_AndSayWhy()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        await fixture.SeedOrderAsync("ord-2", featureIds: "pole-2");
        await fixture.SeedOccupancyAsync(Pole, regulated: 2);
        await fixture.SeedOccupancyAsync("pole-2", regulated: 2);
        await fixture.Sut.StageAsync(OrderId, TargetId(), [OccupancyChangeBuilder.RemoveSlot(2)], Admin, CancellationToken.None);
        await fixture.Sut.StageAsync("ord-2", TargetId("pole-2", "ord-2"), [OccupancyChangeBuilder.Add(slotIndex: 3)], Admin, CancellationToken.None);
        // Someone else's approved order empties slot 2 of the first pole and fills slot 3 of the second.
        await fixture.SeedOccupancyAsync(Pole, regulated: 1);
        await fixture.SeedOccupancyAsync("pole-2", regulated: 3);

        var missing = await fixture.Sut.GetAsync(OrderId, TargetId(), Admin, CancellationToken.None);
        var occupied = await fixture.Sut.GetAsync("ord-2", TargetId("pole-2", "ord-2"), Admin, CancellationToken.None);

        missing.Preview.Should().BeNull();
        missing.Warnings.Should().Equal(OccupancyProblemCodes.StaleBaseline, OccupancyProblemCodes.EntryNotFound);
        missing.Staged.Should().NotBeNull("the staged set is still there to be reviewed, replaced or discarded");
        occupied.Preview.Should().BeNull();
        occupied.Warnings.Should().Equal(OccupancyProblemCodes.StaleBaseline, OccupancyProblemCodes.InvalidChange);
    }

    public static TheoryData<string, OccupancyChange[]> InvalidChanges => new()
    {
        { "no changes (OCC-G2)", [] },
        { "slot out of range (OCC-G3)", [OccupancyChangeBuilder.Add(slotIndex: 7)] },
        { "two changes on one slot (OCC-G4)", [OccupancyChangeBuilder.RemoveSlot(1), OccupancyChangeBuilder.MarkSlotUnidentified(1)] },
        { "add on an occupied slot (OCC-B1)", [OccupancyChangeBuilder.Add(slotIndex: 1)] },
        { "illegal status move (OCC-B3)", [OccupancyChangeBuilder.ModifySlot(1, PoleAttachmentOccupantStatus.ActiveReservation)] },
        { "reservation beyond the slots", [OccupancyChangeBuilder.Add(null, status: PoleAttachmentOccupantStatus.ActiveReservation)] },
        { "four decimals", [OccupancyChangeBuilder.Add(slotIndex: 5) with { CableDiameterMm = 1.2345m }] },
        { "an add without an id", [OccupancyChangeBuilder.Add(slotIndex: 5) with { EntryId = null }] },
    };

    [Theory]
    [MemberData(nameof(InvalidChanges))]
    public async Task StageAsync_ChangesThatAreMalformedOrDoNotApply_ThrowInvalidOccupancyChange_AndStageNothing(string why, OccupancyChange[] changes)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        await fixture.SeedOccupancyAsync(regulated: 2);

        var act = () => fixture.Sut.StageAsync(OrderId, TargetId(), changes, Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOccupancyChangeException>(why)).Which.InnerException.Should().NotBeNull();
        fixture.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task StageAsync_ChangeOnAnEmptySlot_OrOnAnEntryNotOnThePole_ThrowsOccupancyEntryNotFound_AndStagesNothing()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        await fixture.SeedOccupancyAsync(regulated: 2);
        var unknownEntry = Guid.NewGuid();

        var emptySlot = () => fixture.Sut.StageAsync(OrderId, TargetId(), [OccupancyChangeBuilder.RemoveSlot(5)], Admin, CancellationToken.None);
        var noSuchEntry = () => fixture.Sut.StageAsync(OrderId, TargetId(), [OccupancyChangeBuilder.MarkEntryUnidentified(unknownEntry)], Admin, CancellationToken.None);

        (await emptySlot.Should().ThrowAsync<OccupancyEntryNotFoundException>()).Which.SlotIndex.Should().Be(5);
        (await noSuchEntry.Should().ThrowAsync<OccupancyEntryNotFoundException>()).Which.EntryId.Should().Be(unknownEntry);
        fixture.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task StageAsync_OverCapacityEntries_CanBeAddedModifiedMarkedAndRemoved_ByTheirIds()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        var live = await fixture.SeedOccupancyAsync(regulated: 6, beyond: 3);
        var beyond = live.OverCapacityEntries;

        var view = await fixture.Sut.StageAsync(
            OrderId, TargetId(),
            [
                OccupancyChangeBuilder.AddBeyondRegulatedSlots(),
                OccupancyChangeBuilder.ModifyEntry(beyond[0].Id, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
                OccupancyChangeBuilder.MarkEntryUnidentified(beyond[1].Id),
                OccupancyChangeBuilder.RemoveEntry(beyond[2].Id),
            ],
            Admin, CancellationToken.None);

        view.Preview!.Entries.Count(entry => entry.SlotIndex is null).Should().Be(3);
        view.Preview.Entries.Single(entry => entry.Id == beyond[0].Id).Status.Should().Be(PoleAttachmentOccupantStatus.AbandonedDeadDrop);
        view.Preview.Entries.Single(entry => entry.Id == beyond[1].Id).IsUnidentified.Should().BeTrue();
        view.Preview.Entries.Should().NotContain(entry => entry.Id == beyond[2].Id);
        view.Preview.Should().BeEquivalentTo(new { OccupiedCount = 9, IsOverCapacity = true, HasUnidentifiedOccupant = true });
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public async Task StageAsync_OnAnOrderInATerminalState_ThrowsInvalidServiceOrderTransition_AndStagesNothing(string status)
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync(status: status);

        var act = () => fixture.Sut.StageAsync(OrderId, TargetId(), OneAdd, Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which.Should().BeEquivalentTo(new { From = status, To = status });
        fixture.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task StageAsync_OnAReleasedTargetOfAnOpenOrder_ThrowsOccupancyTargetReleased()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        fixture.ChangeSets.MarkReleased(TargetId());

        var act = () => fixture.Sut.StageAsync(OrderId, TargetId(), OneAdd, Admin, CancellationToken.None);

        await act.Should().ThrowAsync<OccupancyTargetReleasedException>();
        fixture.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_OnAClosedOrder_StillReads_AndShowsNothingStaged()
    {
        var fixture = new OccupancyStagingFixture();
        var order = await fixture.SeedOrderAsync();
        await fixture.SeedOccupancyAsync(regulated: 2);
        await fixture.Sut.StageAsync(OrderId, TargetId(), OneAdd, Admin, CancellationToken.None);
        order.Status = ServiceOrderStatus.Cancelled;

        var view = await fixture.Sut.GetAsync(OrderId, TargetId(), Admin, CancellationToken.None);
        var discarded = await fixture.Sut.GetPageAsync(OrderId, 1, 50, OccupancyChangeSetStatus.Discarded, Admin, CancellationToken.None);

        view.Staged.Should().BeNull("what was staged was discarded when the order closed");
        view.Current.OccupiedCount.Should().Be(2);
        discarded.Items.Should().ContainSingle().Which.ClosedAt.Should().NotBeNull();
        (await fixture.Sut.DiscardAsync(OrderId, TargetId(), Admin, CancellationToken.None)).Should().BeFalse("discarding on a closed order is harmless");
    }

    [Fact]
    public async Task StageAsync_CancelledBeforeTheWrite_StagesNothing()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => fixture.Sut.StageAsync(OrderId, TargetId(), OneAdd, Admin, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.ChangeSets.Snapshot().Should().BeEmpty();
    }

    // ── Discard and list ──────────────────────────────────────────────────────

    [Fact]
    public async Task DiscardAsync_DropsTheTargetsStagedSet_AndDiscardAllAsync_EveryStagedSetOfTheOrder()
    {
        var fixture = new OccupancyStagingFixture();
        await fixture.SeedOrderAsync(featureIds: ["pole-1", "pole-2", "pole-3"]);
        foreach (var pole in new[] { "pole-1", "pole-2", "pole-3" })
            await fixture.Sut.StageAsync(OrderId, TargetId(pole), OneAdd, Admin, CancellationToken.None);

        var one = await fixture.Sut.DiscardAsync(OrderId, TargetId("pole-2"), Admin, CancellationToken.None);
        var again = await fixture.Sut.DiscardAsync(OrderId, TargetId("pole-2"), Admin, CancellationToken.None);
        var rest = await fixture.Sut.DiscardAllAsync(OrderId, Admin, CancellationToken.None);

        one.Should().BeTrue();
        again.Should().BeFalse();
        rest.Should().Be(2);
        fixture.ChangeSets.Snapshot().Should().HaveCount(3).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Discarded);
        (await fixture.Sut.GetAsync(OrderId, TargetId("pole-1"), Admin, CancellationToken.None)).Staged.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_PagesTheOrdersSets_ByPole_FilteredByStatusWhenAsked()
    {
        var fixture = new OccupancyStagingFixture();
        string[] poles = ["pole-c", "pole-a", "pole-e", "pole-b", "pole-d"];
        await fixture.SeedOrderAsync(featureIds: poles);
        foreach (var pole in poles)
            await fixture.Sut.StageAsync(OrderId, TargetId(pole), OneAdd, Admin, CancellationToken.None);
        await fixture.Sut.DiscardAsync(OrderId, TargetId("pole-b"), Admin, CancellationToken.None);

        PagedResult<OccupancyChangeSetView> staged = await fixture.Sut.GetPageAsync(OrderId, 2, 2, OccupancyChangeSetStatus.Staged, Viewer, CancellationToken.None);
        var all = await fixture.Sut.GetPageAsync(OrderId, 1, 50, null, Viewer, CancellationToken.None);

        staged.TotalCount.Should().Be(4);
        staged.Items.Select(set => set.PoleFeatureId).Should().Equal("pole-d", "pole-e");
        all.TotalCount.Should().Be(5);
        all.Items.Select(set => set.PoleFeatureId).Should().Equal("pole-a", "pole-b", "pole-c", "pole-d", "pole-e");
        all.Items.Single(set => string.Equals(set.PoleFeatureId, "pole-b", StringComparison.Ordinal)).Status.Should().Be(OccupancyChangeSetStatus.Discarded);
    }

    // ── Construction and DI ───────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        var f = new OccupancyStagingFixture();

        ((Action)(() => _ = new OrderOccupancyStagingService(null!, f.OrderTypes, f.Rules, f.Targets, f.Occupancy, f.ChangeSets, f.ChangeSets, f.Options, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, null!, f.Rules, f.Targets, f.Occupancy, f.ChangeSets, f.ChangeSets, f.Options, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, f.OrderTypes, null!, f.Targets, f.Occupancy, f.ChangeSets, f.ChangeSets, f.Options, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, f.OrderTypes, f.Rules, null!, f.Occupancy, f.ChangeSets, f.ChangeSets, f.Options, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, f.OrderTypes, f.Rules, f.Targets, null!, f.ChangeSets, f.ChangeSets, f.Options, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Occupancy, null!, f.ChangeSets, f.Options, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Occupancy, f.ChangeSets, null!, f.Options, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Occupancy, f.ChangeSets, f.ChangeSets, null!, f.Clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderOccupancyStagingService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Occupancy, f.ChangeSets, f.ChangeSets, f.Options, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AddOrderOccupancyStaging_RegistersOneScopedServicePerScope_WithTheHostsPoleAssetTypes()
    {
        var f = new OccupancyStagingFixture();
        var services = new ServiceCollection();
        services.AddSingleton(f.OrderTypes);
        services.AddSingleton(f.Rules);
        services.AddScoped<IServiceOrderReader>(_ => f.Orders);
        services.AddScoped<IServiceOrderTargetReader>(_ => f.Targets);
        services.AddScoped<IPoleOccupancyReader>(_ => f.Occupancy);
        services.AddScoped<IOccupancyChangeSetReader>(_ => f.ChangeSets);
        services.AddScoped<IOccupancyChangeSetWriter>(_ => f.ChangeSets);
        services.AddOrderOccupancyStaging(options => options.PoleAssetTypeIds.Add("pole"));
        services.AddOrderOccupancyStaging(options => options.PoleAssetTypeIds.Add("ignored: already registered"));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        await using var otherScope = provider.CreateAsyncScope();

        var service = scope.ServiceProvider.GetRequiredService<IOrderOccupancyStagingService>();

        service.Should().BeOfType<OrderOccupancyStagingService>().And.BeSameAs(scope.ServiceProvider.GetRequiredService<OrderOccupancyStagingService>());
        otherScope.ServiceProvider.GetRequiredService<IOrderOccupancyStagingService>().Should().NotBeSameAs(service);
        provider.GetRequiredService<OrderOccupancyOptions>().PoleAssetTypeIds.Should().Equal("pole");
        services.Count(d => d.ServiceType == typeof(IOrderOccupancyStagingService)).Should().Be(1);
        ((Action)(() => WorkflowServiceExtensions.AddOrderOccupancyStaging(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddOrderOccupancyStaging_WithoutConfiguration_NamesNoPoleAssetType()
    {
        var services = new ServiceCollection();
        services.AddOrderOccupancyStaging();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<OrderOccupancyOptions>().PoleAssetTypeIds.Should().BeEmpty();
    }

    /// <summary>Every store port, failing on any call: proof that a refusal came before any store was touched.</summary>
    private sealed class ThrowingStores : IPoleOccupancyReader, IOccupancyChangeSetReader, IOccupancyChangeSetWriter, IServiceOrderTargetReader
    {
        private static InvalidOperationException Touched() => new("A store was touched before the caller was authorized.");

        public Task<PoleOccupancy?> GetForPoleAsync(string poleFeatureId, CancellationToken ct) => throw Touched();
        public Task<IReadOnlyList<PoleOccupancy>> GetForPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct) => throw Touched();
        public Task<IReadOnlyList<PoleOccupantPresence>> FindContractedOccupantsAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct) => throw Touched();
        public Task<PagedResult<PoleOccupancy>> GetPageAsync(PoleOccupancyQuery query, CancellationToken ct) => throw Touched();
        public Task<OccupancyChangeSet?> GetStagedAsync(string orderId, string targetId, CancellationToken ct) => throw Touched();
        public Task<PagedResult<OccupancyChangeSet>> GetPageAsync(OccupancyChangeSetQuery query, CancellationToken ct) => throw Touched();
        public Task<OccupancyChangeSet> StageAsync(OccupancyChangeSet changeSet, CancellationToken ct) => throw Touched();
        public Task<bool> DiscardAsync(string orderId, string targetId, CancellationToken ct) => throw Touched();
        public Task<int> DiscardAllAsync(string orderId, CancellationToken ct) => throw Touched();
        public Task<bool> MarkCommittedAsync(OccupancyChangeSet committed, CancellationToken ct) => throw Touched();
        public Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct) => throw Touched();
        public Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct) => throw Touched();
        public Task<int> CountAsync(string orderId, CancellationToken ct) => throw Touched();
        public Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) => throw Touched();
        public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct) => throw Touched();
    }
}
