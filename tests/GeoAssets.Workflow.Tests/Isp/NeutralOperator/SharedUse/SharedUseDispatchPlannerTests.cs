using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator.SharedUse;

/// <summary>
/// <see cref="SharedUseDispatchPlanner"/> on plain values: every guard and branch of the plan
/// (XD01-316, DISP-G1/G4/G6..G9, DISP-B1..B9, DISP-B14), with no store and no host involved.
/// </summary>
public sealed class SharedUseDispatchPlannerTests
{
    private const string Parent = "ord-1";
    private const string Pole = "pole";

    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000316");
    private static readonly Guid One = new("c1c1c1c1-0000-4000-8000-000000000316");
    private static readonly Guid Two = new("c2c2c2c2-0000-4000-8000-000000000316");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    private static readonly OrderType ParentType = SharedUseOrderTypes.CreatePoleWork();
    private static readonly OrderType ChildType = SharedUseOrderTypes.CreateOccupantNotice();

    private readonly FakeTimeProvider _clock = new(Now);

    // ── ChildOrderId ──────────────────────────────────────────────────────────

    [Fact]
    public void ChildOrderId_IsTheSameOnEveryCall_FitsAnOrderId_AndDiffersPerParentAndPerOccupant()
    {
        var id = SharedUseDispatchPlanner.ChildOrderId(Parent, One);

        id.Should().Be(SharedUseDispatchPlanner.ChildOrderId(Parent, One));
        id.Should().HaveLength(36);
        Guid.TryParse(id, out _).Should().BeTrue();
        id.Should().NotBe(SharedUseDispatchPlanner.ChildOrderId(Parent, Two));
        id.Should().NotBe(SharedUseDispatchPlanner.ChildOrderId("ord-2", One));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ChildOrderId_BlankParent_ThrowsArgumentException(string parentOrderId) =>
        ((Action)(() => SharedUseDispatchPlanner.ChildOrderId(parentOrderId, One))).Should().Throw<ArgumentException>();

    [Fact]
    public void ChildOrderId_NullParentOrEmptyOrganization_Throws()
    {
        ((Action)(() => SharedUseDispatchPlanner.ChildOrderId(null!, One))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SharedUseDispatchPlanner.ChildOrderId(Parent, Guid.Empty))).Should().Throw<ArgumentException>();
    }

    // ── StatusAfterDispatch (DISP-G6) ─────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft, ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.Pending, ServiceOrderStatus.Pending)]
    public void StatusAfterDispatch_TypeWithItsOwnGraph_FollowsTheTransitionTaggedDispatch(string from, string expected) =>
        SharedUseDispatchPlanner.StatusAfterDispatch(ParentType, from).Should().Be(expected);

    [Theory]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    [InlineData("NotAState")]
    public void StatusAfterDispatch_TypeWithItsOwnGraph_NoDispatchTransitionFromTheStatus_Throws(string from)
    {
        var act = () => SharedUseDispatchPlanner.StatusAfterDispatch(ParentType, from);

        var refused = act.Should().Throw<InvalidServiceOrderTransitionException>().Which;
        refused.From.Should().Be(from);
        refused.To.Should().Be(from);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Pending)]
    public void StatusAfterDispatch_TypeWithoutAGraph_MovesADraftToPending_AndLeavesAPendingOrderThere(string from) =>
        SharedUseDispatchPlanner.StatusAfterDispatch(new OrderType { Id = "plain", DisplayName = "Plain" }, from)
            .Should().Be(ServiceOrderStatus.Pending);

    [Theory]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Completed)]
    public void StatusAfterDispatch_TypeWithoutAGraph_FromAnyOtherStatus_Throws(string from)
    {
        var act = () => SharedUseDispatchPlanner.StatusAfterDispatch(new OrderType { Id = "plain", DisplayName = "Plain" }, from);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public void StatusAfterDispatch_NullTypeOrBlankStatus_Throws()
    {
        ((Action)(() => SharedUseDispatchPlanner.StatusAfterDispatch(null!, ServiceOrderStatus.Draft))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SharedUseDispatchPlanner.StatusAfterDispatch(ParentType, " "))).Should().Throw<ArgumentException>();
    }

    // ── Plan: guards (DISP-G1, DISP-G4) ───────────────────────────────────────

    [Fact]
    public void Plan_NullRequestOrClock_ThrowsArgumentNullException()
    {
        ((Action)(() => SharedUseDispatchPlanner.Plan(null!, _clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SharedUseDispatchPlanner.Plan(Request(), null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Plan_BlankDispatcher_ThrowsArgumentException() =>
        ((Action)(() => SharedUseDispatchPlanner.Plan(Request() with { DispatchedBy = " " }, _clock))).Should().Throw<ArgumentException>();

    [Fact]
    public void Plan_ParentTypeWithoutFanOut_ThrowsInvalidOperationException()
    {
        var noPolicy = new OrderType { Id = "plain", DisplayName = "Plain" };
        var noFanOut = new OrderType { Id = "targets-only", DisplayName = "Targets", TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = [Pole] } };

        ((Action)(() => SharedUseDispatchPlanner.Plan(Request() with { ParentType = noPolicy }, _clock))).Should().Throw<InvalidOperationException>();
        ((Action)(() => SharedUseDispatchPlanner.Plan(Request() with { ParentType = noFanOut }, _clock))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Plan_ChildTypeOtherThanTheOneTheFanOutNames_ThrowsInvalidOperationException()
    {
        var other = new OrderType { Id = "something-else", DisplayName = "Other" };

        ((Action)(() => SharedUseDispatchPlanner.Plan(Request() with { ChildType = other }, _clock))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Plan_ParentInAStatusWithNoDispatch_ThrowsInvalidServiceOrderTransitionException()
    {
        var request = Request(Target("p1")) with { Parent = ParentOrder(ServiceOrderStatus.Completed) };

        ((Action)(() => SharedUseDispatchPlanner.Plan(request, _clock))).Should().Throw<InvalidServiceOrderTransitionException>();
    }

    // ── Plan: block reasons, in the order they are checked (DISP-G7..G9, DISP-B4) ──

    [Fact]
    public void Plan_NoTargets_IsBlockedAsNoTargets()
    {
        var plan = SharedUseDispatchPlanner.Plan(Request(), _clock);

        plan.BlockReason.Should().Be(SharedUseDispatchBlockReason.NoTargets);
        plan.TargetCount.Should().Be(0);
        plan.Children.Should().BeEmpty();
    }

    [Fact]
    public void Plan_AnOpenTargetWithoutAnActivity_IsBlockedBeforeAnythingElse_ListingEveryOffenderInIdOrder()
    {
        var request = Request(Target("p1", activities: []), Target("p2"), Target("p3", activities: [])) with
        {
            Occupancies = [Occupancy("p2", Contracted("p2", 1, One))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.BlockReason.Should().Be(SharedUseDispatchBlockReason.TargetsWithoutActivity);
        plan.TargetIdsWithoutActivity.Should().BeEquivalentTo(["t-p1", "t-p3"]).And.BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Plan_ABlockedTargetWithoutAnActivity_DoesNotBlockTheDispatch()
    {
        var request = Request(Target("p1", activities: []), Target("p2")) with
        {
            ConflictLevels = Levels(("p1", OrderTargetConflictLevel.Blocked)),
            Occupancies = [Occupancy("p2", Contracted("p2", 1, One))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.BlockReason.Should().BeNull("nobody is asked to work on a blocked pole, so it needs no activity");
        plan.TargetIdsWithoutActivity.Should().BeEmpty();
        plan.BlockedTargetCount.Should().Be(1);
    }

    [Fact]
    public void Plan_EveryTargetBlocked_IsBlockedAsAllTargetsBlocked()
    {
        var request = Request(Target("p1"), Target("p2")) with
        {
            ConflictLevels = Levels(("p1", OrderTargetConflictLevel.Blocked), ("p2", OrderTargetConflictLevel.Blocked)),
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.BlockReason.Should().Be(SharedUseDispatchBlockReason.AllTargetsBlocked);
        plan.BlockedTargetCount.Should().Be(2);
        plan.Children.Should().BeEmpty("occupancy of a blocked pole is not looked at");
        plan.SkippedEntries.Should().BeEmpty();
        plan.UnnotifiablePoleIds.Should().BeEmpty();
    }

    [Fact]
    public void Plan_NoOccupantQualifies_IsBlockedAsNoEligibleOccupants_AndEveryPoleIsUnnotifiable()
    {
        var request = Request(Target("p1"), Target("p2")) with
        {
            Occupancies = [Occupancy("p1", Entry("p1", 1, One, PoleAttachmentOccupantStatus.AbandonedDeadDrop))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.BlockReason.Should().Be(SharedUseDispatchBlockReason.NoEligibleOccupants);
        plan.UnnotifiablePoleIds.Should().Equal("p1", "p2");
    }

    [Fact]
    public void Plan_AWarningLevelConflict_DoesNotLeaveTheTargetOut()
    {
        var request = Request(Target("p1")) with
        {
            ConflictLevels = Levels(("p1", OrderTargetConflictLevel.Warning)),
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.BlockedTargetCount.Should().Be(0);
        plan.Children.Should().ContainSingle().Which.Targets.Should().ContainSingle()
            .Which.ConflictLevel.Should().Be(OrderTargetConflictLevel.Warning, "the child's target carries the level evaluated now");
    }

    [Fact]
    public void Plan_TheConflictLevelStoredOnATarget_IsNotWhatDecides()
    {
        var request = Request(Target("p1") with { ConflictLevel = OrderTargetConflictLevel.Blocked }) with
        {
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.BlockedTargetCount.Should().Be(0, "the stored level is a snapshot; the levels handed in are the live ones");
        plan.BlockReason.Should().BeNull();
    }

    // ── Plan: who is notified (DISP-B2, DISP-B3, DISP-B5) ─────────────────────

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    public void Plan_AnEntryNotUnderContract_IsSkippedAsNoActiveContract_WhateverOrganizationItNames(PoleAttachmentOccupantStatus status)
    {
        var entry = Entry("p1", 2, Two, status);
        var request = Request(Target("p1")) with { Occupancies = [Occupancy("p1", Contracted("p1", 1, One), entry)] };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.SkippedEntries.Should().Equal(new SharedUseSkippedEntry("p1", entry.Id, 2, status, SharedUseSkipReason.NoActiveContract));
        plan.Children.Should().ContainSingle().Which.OccupantOrganizationId.Should().Be(One);
    }

    [Fact]
    public void Plan_AContractedEntryWithNoOrganization_IsSkippedAsUnknownOrganization()
    {
        var entry = Entry("p1", 2, occupant: null, PoleAttachmentOccupantStatus.AuthorizedContracted);
        var request = Request(Target("p1")) with { Occupancies = [Occupancy("p1", Contracted("p1", 1, One), entry)] };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.SkippedEntries.Should().ContainSingle().Which.Reason.Should().Be(SharedUseSkipReason.UnknownOrganization);
    }

    [Fact]
    public void Plan_AnEntryOfThePoleOwnerItself_IsSkippedAsPoleOwner_AndGetsNoChild()
    {
        var entry = Contracted("p1", 1, Owner);
        var request = Request(Target("p1")) with { Occupancies = [Occupancy("p1", entry)] };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.SkippedEntries.Should().Equal(
            new SharedUseSkippedEntry("p1", entry.Id, 1, PoleAttachmentOccupantStatus.AuthorizedContracted, SharedUseSkipReason.PoleOwner));
        plan.Children.Should().BeEmpty();
        plan.UnnotifiablePoleIds.Should().Equal("p1");
        plan.BlockReason.Should().Be(SharedUseDispatchBlockReason.NoEligibleOccupants);
    }

    [Fact]
    public void Plan_AContractedEntryBeyondTheRegulatedSlots_IsNotifiedLikeAnyOther()
    {
        var beyond = Entry("p1", slot: null, Two, PoleAttachmentOccupantStatus.AuthorizedContracted);
        var request = Request(Target("p1")) with { Occupancies = [Occupancy("p1", Contracted("p1", 1, One), beyond)] };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.Children.Select(c => c.OccupantOrganizationId).Should().BeEquivalentTo([One, Two]);
        plan.SkippedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ASkippedEntryBeyondTheRegulatedSlots_IsReportedWithNoSlot()
    {
        var beyond = Entry("p1", slot: null, Two, PoleAttachmentOccupantStatus.ClandestineUnauthorized);
        var request = Request(Target("p1")) with { Occupancies = [Occupancy("p1", Contracted("p1", 1, One), beyond)] };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.SkippedEntries.Should().ContainSingle().Which.SlotIndex.Should().BeNull();
    }

    [Fact]
    public void Plan_APoleWithNoOccupancyOnFile_OrWithAnEmptyOne_IsUnnotifiable_AndDoesNotStopTheOthers()
    {
        var request = Request(Target("p1"), Target("p2"), Target("p3")) with
        {
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One)), Occupancy("p3")],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.UnnotifiablePoleIds.Should().Equal("p2", "p3");
        plan.BlockReason.Should().BeNull();
        plan.Children.Should().ContainSingle();
    }

    [Fact]
    public void Plan_OccupancyOfAPoleThatIsNotATarget_IsIgnored()
    {
        var request = Request(Target("p1")) with
        {
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One)), Occupancy("elsewhere", Contracted("elsewhere", 1, Two))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.Children.Should().ContainSingle().Which.OccupantOrganizationId.Should().Be(One);
    }

    [Fact]
    public void Plan_AnOccupantWithSeveralContractedEntriesOnOnePole_GetsThatPoleOnce()
    {
        var request = Request(Target("p1")) with
        {
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One), Contracted("p1", 2, One), Entry("p1", null, One, PoleAttachmentOccupantStatus.AuthorizedContracted))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.Children.Should().ContainSingle().Which.Targets.Should().ContainSingle();
    }

    // ── Plan: what a child is (DISP-B6..B9, DISP-B14) ─────────────────────────

    [Fact]
    public void Plan_EachChild_HoldsOnlyItsOccupantsPoles_InFeatureOrder_AndChildrenComeInOrganizationOrder()
    {
        var request = Request(Target("p3"), Target("p1"), Target("p2")) with
        {
            Occupancies =
            [
                Occupancy("p1", Contracted("p1", 1, Two)),
                Occupancy("p2", Contracted("p2", 1, Two), Contracted("p2", 2, One)),
                Occupancy("p3", Contracted("p3", 1, One)),
            ],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.Children.Select(c => c.OccupantOrganizationId).Should().Equal(One, Two);
        plan.Children[0].Targets.Select(t => t.FeatureId).Should().Equal("p2", "p3");
        plan.Children[1].Targets.Select(t => t.FeatureId).Should().Equal("p1", "p2");
        plan.TargetCount.Should().Be(3);
    }

    [Fact]
    public void Plan_AChildOrder_IsOwnedByTheParentsOrganization_DerivedFromTheParent_AndDispatchedOnceToItsOccupant()
    {
        var request = Request(Target("p1")) with { Occupancies = [Occupancy("p1", Contracted("p1", 1, One))], ActorKind = ActorKind.Agent };

        var child = SharedUseDispatchPlanner.Plan(request, _clock).Children.Should().ContainSingle().Subject;

        child.ChildOrderId.Should().Be(SharedUseDispatchPlanner.ChildOrderId(Parent, One));
        child.AlreadyDispatched.Should().BeFalse();
        var order = child.Order;
        order.Id.Should().Be(child.ChildOrderId);
        order.OrganizationId.Should().Be(Owner);
        order.ParentOrderId.Should().Be(Parent);
        order.OrderTypeId.Should().Be(SharedUseOrderTypes.OccupantNoticeId);
        order.Status.Should().Be(ServiceOrderStatus.Pending, "the child type's initial state");
        order.Title.Should().Be("Clear the lane");
        order.Description.Should().Be("For the new feeder.");
        order.Priority.Should().Be(ServiceOrderPriority.High);
        order.ScheduledAt.Should().Be(Now.UtcDateTime.AddDays(7));
        order.CreatedBy.Should().Be("dispatcher-1");
        order.CreatedAt.Should().Be(Now.UtcDateTime);
        order.Attributes.Should().ContainSingle()
            .Which.Should().Be(KeyValuePair.Create(SharedUseDispatchPlanner.PartyOrganizationAttribute, One.ToString("D")));
        order.ActionLog.Should().BeEmpty();
        var dispatch = order.Dispatches.Should().ContainSingle().Subject;
        dispatch.Should().Be(new OrderDispatch(One.ToString("D"), DispatchTargetType.Organization, "dispatcher-1", Now.UtcDateTime) { ActorKind = ActorKind.Agent });
    }

    [Fact]
    public void Plan_AChildTypeWithNoInitialState_StartsItsOrdersAsDrafts()
    {
        var childType = new OrderType { Id = SharedUseOrderTypes.OccupantNoticeId, DisplayName = "Notice" };
        var request = Request(Target("p1")) with { ChildType = childType, Occupancies = [Occupancy("p1", Contracted("p1", 1, One))] };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.Children.Should().ContainSingle().Which.Order.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public void Plan_TheParentsScope_IsCopiedToEveryChild_IfAndOnlyIfTheParentHasOne()
    {
        var scoped = ParentOrder();
        scoped.Attributes["scopeId"] = "district-7";
        scoped.Attributes["costCentre"] = "cc-1";
        var request = Request(Target("p1")) with { Parent = scoped, Occupancies = [Occupancy("p1", Contracted("p1", 1, One))] };

        var child = SharedUseDispatchPlanner.Plan(request, _clock).Children.Should().ContainSingle().Subject;

        child.Order.Attributes.Should().Contain("scopeId", "district-7");
        child.Order.Attributes.Should().NotContainKey("costCentre", "only the scope travels with the order");
    }

    [Fact]
    public void Plan_AChildTarget_CopiesThePoleAndItsActivities_UnderTheChildOrder()
    {
        var pole = Target("p1", activities: ["relocate-attachment", "identify-attachment"]) with { Note = "mind the dog", Status = OrderTargetStatus.InProgress, Version = 4 };
        var request = Request(pole) with { Occupancies = [Occupancy("p1", Contracted("p1", 1, One))] };

        var child = SharedUseDispatchPlanner.Plan(request, _clock).Children.Should().ContainSingle().Subject;

        var target = child.Targets.Should().ContainSingle().Subject;
        target.Should().BeEquivalentTo(new ServiceOrderTarget
        {
            Id = OrderTargetSelectionService.TargetId(child.ChildOrderId, "p1"),
            OrderId = child.ChildOrderId,
            FeatureId = "p1",
            AssetTypeId = Pole,
            Alias = "B-p1",
            ActivityIds = ["relocate-attachment", "identify-attachment"],
            Status = OrderTargetStatus.Pending,
            ConflictLevel = OrderTargetConflictLevel.None,
            Note = null,
            Longitude = pole.Longitude,
            Latitude = pole.Latitude,
            AddedAt = Now.UtcDateTime,
            AddedBy = "dispatcher-1",
            OrganizationId = Owner,
            Version = 0,
        });
    }

    [Fact]
    public void Plan_AChildTheParentAlreadyHas_IsMarkedAlreadyDispatched_AndIsNotAmongTheNewOnes()
    {
        var request = Request(Target("p1")) with
        {
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One), Contracted("p1", 2, Two))],
            ExistingChildOrderIds = [SharedUseDispatchPlanner.ChildOrderId(Parent, One), "some-other-child"],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.Children.Select(c => (c.OccupantOrganizationId, c.AlreadyDispatched)).Should().Equal((One, true), (Two, false));
        plan.NewChildren.Should().ContainSingle().Which.OccupantOrganizationId.Should().Be(Two);
    }

    [Fact]
    public void Plan_EveryInstant_IsOneReadingOfTheClock_AndTheSameRequestGivesTheSamePlan()
    {
        var request = Request(Target("p1"), Target("p2")) with
        {
            Occupancies = [Occupancy("p1", Contracted("p1", 1, One)), Occupancy("p2", Contracted("p2", 1, Two))],
        };

        var plan = SharedUseDispatchPlanner.Plan(request, _clock);
        var again = SharedUseDispatchPlanner.Plan(request, _clock);

        plan.PlannedAt.Should().Be(Now);
        plan.Children.Should().OnlyContain(c =>
            c.Order.CreatedAt == Now.UtcDateTime
            && c.Order.Dispatches.All(d => d.DispatchedAt == Now.UtcDateTime)
            && c.Targets.All(t => t.AddedAt == Now.UtcDateTime));
        plan.Children.Should().OnlyContain(c => c.Order.CreatedAt.Kind == DateTimeKind.Utc);
        again.Children.Select(c => c.ChildOrderId).Should().Equal(plan.Children.Select(c => c.ChildOrderId));
        again.ParentStatusAfterDispatch.Should().Be(ServiceOrderStatus.Pending);
    }

    // ── EnsureDispatchable ────────────────────────────────────────────────────

    [Fact]
    public void EnsureDispatchable_OnAPlanThatIsNotBlocked_DoesNothing()
    {
        var request = Request(Target("p1")) with { Occupancies = [Occupancy("p1", Contracted("p1", 1, One))] };

        var act = () => SharedUseDispatchPlanner.Plan(request, _clock).EnsureDispatchable();

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureDispatchable_OnABlockedPlan_ThrowsWithTheReason_AndAtMostAHundredOffendingTargets()
    {
        var targets = Enumerable.Range(0, 130).Select(i => Target($"p{i:D3}", activities: [])).ToArray();
        var plan = SharedUseDispatchPlanner.Plan(Request(targets), _clock);

        var act = plan.EnsureDispatchable;

        var blocked = act.Should().Throw<SharedUseDispatchBlockedException>().Which;
        blocked.OrderId.Should().Be(Parent);
        blocked.Reason.Should().Be(SharedUseDispatchBlockReason.TargetsWithoutActivity);
        blocked.TargetIds.Should().HaveCount(SharedUseDispatchBlockedException.MaxReportedTargetIds);
        blocked.TargetIds.Should().Equal(plan.TargetIdsWithoutActivity.Take(100));
        blocked.TargetCount.Should().Be(130);
    }

    // ── Builders ──────────────────────────────────────────────────────────────

    private static ServiceOrder ParentOrder(string status = ServiceOrderStatus.Draft) => new()
    {
        Id = Parent,
        Title = "Clear the lane",
        Description = "For the new feeder.",
        OrderTypeId = SharedUseOrderTypes.PoleWorkId,
        Status = status,
        Priority = ServiceOrderPriority.High,
        CreatedBy = "planner-1",
        OrganizationId = Owner,
        CreatedAt = Now.UtcDateTime.AddDays(-2),
        ScheduledAt = Now.UtcDateTime.AddDays(7),
    };

    private static SharedUseDispatchPlanRequest Request(params ServiceOrderTarget[] targets) => new()
    {
        Parent = ParentOrder(),
        ParentType = ParentType,
        ChildType = ChildType,
        Targets = targets,
        ConflictLevels = Levels(),
        Occupancies = [],
        ExistingChildOrderIds = [],
        DispatchedBy = "dispatcher-1",
    };

    private static ServiceOrderTarget Target(string pole, string[]? activities = null) => new ServiceOrderTargetBuilder()
        .WithId($"t-{pole}")
        .WithOrderId(Parent)
        .WithFeatureId(pole)
        .WithAssetTypeId(Pole)
        .WithAlias($"B-{pole}")
        .WithActivityIds(activities ?? ["relocate-attachment"])
        .WithOrganizationId(Owner)
        .Build();

    private static Dictionary<string, OrderTargetConflictLevel> Levels(params (string Pole, OrderTargetConflictLevel Level)[] levels) =>
        levels.ToDictionary(l => l.Pole, l => l.Level, StringComparer.Ordinal);

    private static PoleOccupancyEntry Contracted(string pole, int slot, Guid occupant) =>
        Entry(pole, slot, occupant, PoleAttachmentOccupantStatus.AuthorizedContracted);

    private static PoleOccupancyEntry Entry(string pole, int? slot, Guid? occupant, PoleAttachmentOccupantStatus status)
    {
        var builder = new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(Owner).WithStatus(status).OccupiedBy(occupant);
        return (slot is { } index ? builder.OnSlot(index) : builder.BeyondRegulatedSlots()).Build();
    }

    private static PoleOccupancy Occupancy(string pole, params PoleOccupancyEntry[] entries) =>
        PoleOccupancy.Create(pole, Owner, entries, PoleOccupancy.FirstVersion, Now);
}
