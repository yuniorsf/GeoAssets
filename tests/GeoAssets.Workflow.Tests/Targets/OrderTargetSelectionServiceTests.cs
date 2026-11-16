using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Workflow.Tests.Targets.SelectionServiceFixture;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// What <see cref="OrderTargetSelectionService"/> does with a selection once the guards have
/// passed (SEL-B5..B15): de-duplication, the selection limit, conflicts, what is written and in
/// which order, and the clock.
/// </summary>
public sealed class OrderTargetSelectionServiceTests
{
    // ── SEL-B5: already in the order ──────────────────────────────────────────

    [Fact]
    public async Task SEL_B5_Preview_FeaturesTheOrderAlreadyHolds_AreCountedAndLeftOutOfTheEligibleSet()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(8));
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "p-002", "p-005", "p-007");

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.Matched.Should().Be(8);
        preview.AlreadyInOrder.Should().Be(3);
        preview.Eligible.Should().Be(5);
        preview.TruncatedByLimit.Should().Be(0);
        preview.FirstPage.TotalCount.Should().Be(5);
        preview.FirstPage.Items.Select(t => t.FeatureId).Should().Equal("p-001", "p-003", "p-004", "p-006", "p-008");
    }

    [Fact]
    public async Task SEL_B5_Apply_AddsOnlyTheFeaturesNotHeldYet_AndLeavesTheHeldTargetsAsTheyWere()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(8));
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "p-002", "p-005", "p-007");

        var result = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        result.Should().BeEquivalentTo(new { Matched = 8, AlreadyInOrder = 3, Eligible = 5, Added = 5, TruncatedByLimit = 0 });
        fixture.Store.Snapshot().Should().HaveCount(8);
        fixture.Stored("p-002").Id.Should().Be("ord-1:p-002");
    }

    [Fact]
    public async Task SEL_B5_Apply_EverythingAlreadyHeld_AddsNothing_AndIsNotAnError()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(3));
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "p-001", "p-002", "p-003");

        var result = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        result.Added.Should().Be(0);
        result.Eligible.Should().Be(0);
        fixture.Store.Snapshot().Should().HaveCount(3);
    }

    [Fact]
    public async Task SEL_B5_OrderHoldingMoreTargetsThanOneReaderPage_IsDeDuplicatedAgainstAllOfThem()
    {
        var poles = Poles(450);
        using var fixture = new SelectionServiceFixture(answer: poles);
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, [.. poles.Take(430).Select(p => p.Id)]);

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.AlreadyInOrder.Should().Be(430);
        preview.Eligible.Should().Be(20);
    }

    [Fact]
    public async Task SEL_B5_TheSameFeatureReturnedTwiceByTheStrategy_IsOneMatch()
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("p-1"), Pole("p-1"), Pole("p-2")]);
        await fixture.SeedOrderAsync();

        var result = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        result.Matched.Should().Be(2);
        result.Added.Should().Be(2);
    }

    // ── SEL-B6 / SEL-B7: selection limit ──────────────────────────────────────

    [Theory]
    [InlineData(7, 0)] // below: 2 held + 7 new = 9 of 10
    [InlineData(8, 0)] // exactly at: 2 + 8 = 10 of 10
    [InlineData(9, 1)] // above: 2 + 9 = 11 of 10
    public async Task SEL_B6_Preview_ReportsHowManyEligibleFeaturesDoNotFitUnderTheLimit(int newFeatures, int expectedTruncated)
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: 10), answer: Poles(newFeatures));
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "held-1", "held-2");

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.Eligible.Should().Be(newFeatures);
        preview.TruncatedByLimit.Should().Be(expectedTruncated);
        preview.FirstPage.TotalCount.Should().Be(newFeatures - expectedTruncated);
        fixture.Store.Snapshot().Should().HaveCount(2, "a preview writes nothing");
    }

    [Fact]
    public async Task SEL_B6_Preview_AboveTheLimit_ShowsTheFirstFeaturesByIdThatFit()
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: 3), answer: [Pole("p-d"), Pole("p-b"), Pole("p-a"), Pole("p-c"), Pole("p-e")]);
        await fixture.SeedOrderAsync();

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.TruncatedByLimit.Should().Be(2);
        preview.FirstPage.Items.Select(t => t.FeatureId).Should().Equal("p-a", "p-b", "p-c");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public async Task SEL_B6_Apply_BelowOrExactlyAtTheLimit_AddsEverything(int newFeatures)
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: 10), answer: Poles(newFeatures));
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "held-1", "held-2");

        var result = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        result.Added.Should().Be(newFeatures);
        result.TruncatedByLimit.Should().Be(0);
        fixture.Store.Snapshot().Should().HaveCount(2 + newFeatures);
    }

    [Fact]
    public async Task SEL_B6_Apply_AboveTheLimitWithoutAcceptTruncation_ThrowsSelectionLimitExceeded_AndWritesNothing()
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: 10), answer: Poles(9));
        var order = await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "held-1", "held-2");

        var act = () => fixture.Sut.ApplyAsync(FenceRequest(acceptTruncation: false), Admin, CancellationToken.None);

        var exceeded = (await act.Should().ThrowAsync<SelectionLimitExceededException>()).Which;
        exceeded.Should().BeEquivalentTo(new { Limit = 10, Current = 2, Requested = 9 });
        fixture.Store.Snapshot().Should().HaveCount(2);
        order.SelectionSpec.Should().BeNull();
        order.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SEL_B6_Apply_AboveTheLimitWithAcceptTruncation_AddsTheFirstFeaturesByIdThatFit_AndReportsTheRest()
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: 10), answer: Poles(9));
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "held-1", "held-2");

        var result = await fixture.Sut.ApplyAsync(FenceRequest(acceptTruncation: true), Admin, CancellationToken.None);

        result.Should().BeEquivalentTo(new { Added = 8, Eligible = 9, TruncatedByLimit = 1 });
        fixture.Store.Snapshot().Should().HaveCount(10);
        fixture.Store.Snapshot().Select(t => t.FeatureId).Should().NotContain("p-009").And.Contain("p-008");
    }

    [Fact]
    public async Task SEL_B6_NoSelectionLimit_NeverTruncates()
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: null), answer: Poles(300));
        await fixture.SeedOrderAsync();

        var result = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        result.Added.Should().Be(300);
        result.TruncatedByLimit.Should().Be(0);
    }

    [Fact]
    public async Task SEL_B6_FeaturesAlreadyHeld_DoNotConsumeTheRemainingCapacityAgain()
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: 3), answer: [Pole("p-1"), Pole("p-2"), Pole("p-3")]);
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "p-1", "p-2");

        var result = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        result.Should().BeEquivalentTo(new { Added = 1, AlreadyInOrder = 2, TruncatedByLimit = 0 });
    }

    [Fact]
    public async Task SEL_B7_OrderAlreadyAtItsLimit_PreviewTruncatesEverything_ApplyRejects_AcceptTruncationAddsNothing()
    {
        using var fixture = new SelectionServiceFixture(Policy(selectionLimit: 2), answer: Poles(4));
        await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "held-1", "held-2");

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);
        var reject = () => fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);
        var truncated = await fixture.Sut.ApplyAsync(FenceRequest(acceptTruncation: true), Admin, CancellationToken.None);

        preview.Should().BeEquivalentTo(new { Eligible = 4, TruncatedByLimit = 4 });
        preview.FirstPage.Items.Should().BeEmpty();
        (await reject.Should().ThrowAsync<SelectionLimitExceededException>())
            .Which.Should().BeEquivalentTo(new { Limit = 2, Current = 2, Requested = 4 });
        truncated.Should().BeEquivalentTo(new { Added = 0, TruncatedByLimit = 4 });
        fixture.Store.Snapshot().Should().HaveCount(2);
    }

    // ── SEL-B8 and the exclusivity matrix ─────────────────────────────────────

    public static TheoryData<OrderTargetExclusivity, string, OrderTargetConflictLevel> ExclusivityMatrix() => new()
    {
        // own exclusivity, who else holds the feature, expected level
        { OrderTargetExclusivity.None, "nobody", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.WarnOnOverlap, "nobody", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.BlockOnOverlap, "nobody", OrderTargetConflictLevel.None },

        { OrderTargetExclusivity.None, "same-type-open", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.WarnOnOverlap, "same-type-open", OrderTargetConflictLevel.Warning },
        { OrderTargetExclusivity.BlockOnOverlap, "same-type-open", OrderTargetConflictLevel.Blocked },

        { OrderTargetExclusivity.None, "other-type-open", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.WarnOnOverlap, "other-type-open", OrderTargetConflictLevel.Warning },
        { OrderTargetExclusivity.BlockOnOverlap, "other-type-open", OrderTargetConflictLevel.Blocked },

        { OrderTargetExclusivity.None, "other-type-warning-open", OrderTargetConflictLevel.Warning },
        { OrderTargetExclusivity.None, "other-type-blocking-open", OrderTargetConflictLevel.Blocked },
        { OrderTargetExclusivity.WarnOnOverlap, "other-type-blocking-open", OrderTargetConflictLevel.Blocked },

        { OrderTargetExclusivity.WarnOnOverlap, "same-type-released", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.BlockOnOverlap, "same-type-released", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.BlockOnOverlap, "other-type-blocking-released", OrderTargetConflictLevel.None },

        { OrderTargetExclusivity.WarnOnOverlap, "parent-open", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.BlockOnOverlap, "parent-open", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.BlockOnOverlap, "child-open", OrderTargetConflictLevel.None },
        { OrderTargetExclusivity.BlockOnOverlap, "sibling-open", OrderTargetConflictLevel.None },
    };

    [Theory]
    [MemberData(nameof(ExclusivityMatrix))]
    public async Task SEL_B8_ConflictLevel_FollowsOwnAndOtherExclusivity_OpenOrReleased_AndIgnoresTheOrdersFamily(
        OrderTargetExclusivity own, string heldBy, OrderTargetConflictLevel expected)
    {
        ArgumentNullException.ThrowIfNull(heldBy);

        using var fixture = new SelectionServiceFixture(Policy(exclusivity: own), answer: [Pole("p-1"), Pole("p-2")]);
        await ArrangeOtherHolderAsync(fixture, heldBy);

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);
        var result = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        fixture.Stored("p-1").ConflictLevel.Should().Be(expected);
        fixture.Stored("p-2").ConflictLevel.Should().Be(OrderTargetConflictLevel.None, "nobody else holds it");
        Of(preview.FirstPage.Items, "p-1").ConflictLevel.Should().Be(expected);

        var warnings = expected == OrderTargetConflictLevel.Warning ? 1 : 0;
        var blocked = expected == OrderTargetConflictLevel.Blocked ? 1 : 0;
        preview.Should().BeEquivalentTo(new { WarningCount = warnings, BlockedCount = blocked });
        result.Should().BeEquivalentTo(new { WarningCount = warnings, BlockedCount = blocked, Added = 2 });
    }

    /// <summary>Seeds the order under test and, per <paramref name="heldBy"/>, another order holding feature <c>p-1</c>.</summary>
    private static async Task ArrangeOtherHolderAsync(SelectionServiceFixture fixture, string heldBy)
    {
        fixture.OrderTypes.Register(new OrderType { Id = "other", DisplayName = "Other", TargetPolicy = Policy() });
        fixture.OrderTypes.Register(new OrderType
        {
            Id = "other-warning", DisplayName = "Other warning", TargetPolicy = Policy(exclusivity: OrderTargetExclusivity.WarnOnOverlap),
        });
        fixture.OrderTypes.Register(new OrderType
        {
            Id = "other-blocking", DisplayName = "Other blocking", TargetPolicy = Policy(exclusivity: OrderTargetExclusivity.BlockOnOverlap),
        });

        var released = heldBy.EndsWith("released", StringComparison.Ordinal);
        var status = released ? ServiceOrderStatus.Completed : ServiceOrderStatus.InProgress;

        switch (heldBy)
        {
            case "nobody":
                await fixture.SeedOrderAsync();
                return;
            case "parent-open":
                await fixture.SeedOrderAsync("ord-other", status: status);
                await fixture.SeedOrderAsync(parentOrderId: "ord-other");
                break;
            case "child-open":
                await fixture.SeedOrderAsync();
                await fixture.SeedOrderAsync("ord-other", status: status, parentOrderId: OrderId);
                break;
            case "sibling-open":
                await fixture.SeedOrderAsync(parentOrderId: "ord-root");
                await fixture.SeedOrderAsync("ord-other", status: status, parentOrderId: "ord-root");
                break;
            default:
                await fixture.SeedOrderAsync();
                var otherType = heldBy switch
                {
                    _ when heldBy.StartsWith("same-type", StringComparison.Ordinal) => OrderTypeId,
                    _ when heldBy.StartsWith("other-type-warning", StringComparison.Ordinal) => "other-warning",
                    _ when heldBy.StartsWith("other-type-blocking", StringComparison.Ordinal) => "other-blocking",
                    _ => "other",
                };
                await fixture.SeedOrderAsync("ord-other", otherType, status);
                break;
        }

        await fixture.HoldAsync("ord-other", "p-1");
    }

    [Fact]
    public async Task SEL_B8_BlockedTargets_AreIncluded_AndCountTowardTheSelectionLimit()
    {
        using var fixture = new SelectionServiceFixture(
            Policy(selectionLimit: 2, exclusivity: OrderTargetExclusivity.BlockOnOverlap), answer: [Pole("p-1"), Pole("p-2"), Pole("p-3")]);
        await fixture.SeedOrderAsync();
        await fixture.SeedOrderAsync("ord-other", status: ServiceOrderStatus.InProgress);
        await fixture.HoldAsync("ord-other", "p-1");

        var reject = () => fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);
        await reject.Should().ThrowAsync<SelectionLimitExceededException>();
        var result = await fixture.Sut.ApplyAsync(FenceRequest(acceptTruncation: true), Admin, CancellationToken.None);

        result.Should().BeEquivalentTo(new { Added = 2, BlockedCount = 1, TruncatedByLimit = 1 });
        fixture.StoredFor().Select(t => t.FeatureId).Should().BeEquivalentTo("p-1", "p-2");
        fixture.Stored("p-1").ConflictLevel.Should().Be(OrderTargetConflictLevel.Blocked);
    }

    // ── SEL-B9 / SEL-B10 / SEL-B11: what is written ───────────────────────────

    [Fact]
    public async Task SEL_B9_Preview_WritesNoTargetNoSelectionSpecAndNoActionLog()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(5));
        var order = await fixture.SeedOrderAsync();
        var updates = 0;
        fixture.Orders.OrderUpdated += (_, _) => updates++;

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.Matched.Should().Be(5);
        fixture.Store.Snapshot().Should().BeEmpty();
        order.SelectionSpec.Should().BeNull();
        order.ActionLog.Should().BeEmpty();
        order.UpdatedAt.Should().BeNull();
        updates.Should().Be(0);
    }

    [Fact]
    public async Task SEL_B9_Preview_PagesTheTargetsThatWouldBeAdded_AndAPagePastTheEndIsEmptyWithTheCountsIntact()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(5));
        await fixture.SeedOrderAsync();

        var second = await fixture.Sut.PreviewAsync(FenceRequest(page: 2, pageSize: 2), Admin, CancellationToken.None);
        var beyond = await fixture.Sut.PreviewAsync(FenceRequest(page: int.MaxValue, pageSize: 200), Admin, CancellationToken.None);

        second.FirstPage.Items.Select(t => t.FeatureId).Should().Equal("p-003", "p-004");
        second.FirstPage.TotalCount.Should().Be(5);
        beyond.FirstPage.Items.Should().BeEmpty();
        beyond.Should().BeEquivalentTo(new { Matched = 5, Eligible = 5 });
        beyond.FirstPage.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task SEL_B10_Apply_AddsTheTargetsFirst_ThenStampsTheSelectionSpec_ThenAppendsOneAnnotateEntryWithTheCounts()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(3));
        var order = await fixture.SeedOrderAsync();
        await fixture.HoldAsync(OrderId, "p-001");
        var observed = new List<(int Targets, bool SpecStamped, int ActionLogEntries)>();
        fixture.Orders.OrderUpdated += (_, updated) =>
            observed.Add((fixture.Store.Snapshot().Count, updated.SelectionSpec is not null, updated.ActionLog.Count));

        await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        observed.Should().Equal(
            (3, true, 0),  // order update: the targets are already stored, the log entry is not yet
            (3, true, 1)); // action-log append

        order.SelectionSpec!.StrategyId.Should().Be(PolygonFenceSelectionStrategy.Id);
        order.SelectionSpec.Parameters.Should().ContainKey(PolygonFenceSelectionStrategy.RingsParameter);

        var entry = order.ActionLog.Should().ContainSingle().Subject;
        entry.Action.Should().Be(OrderActionType.Annotate);
        entry.PerformedBy.Should().Be(Admin.UserId);
        entry.ResultingStatus.Should().BeNull();
        entry.Comment.Should().Be(
            "Target selection 'polygon-fence': matched 3, added 2, already in order 1, truncated 0, warnings 0, blocked 0.");
    }

    [Fact]
    public async Task SEL_B11_RepeatingAnIdenticalApply_AddsZeroRows_AndNoDuplicates()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(4));
        await fixture.SeedOrderAsync();

        var first = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);
        var second = await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        first.Added.Should().Be(4);
        second.Should().BeEquivalentTo(new { Added = 0, AlreadyInOrder = 4, Eligible = 0, Matched = 4 });
        fixture.Store.Snapshot().Should().HaveCount(4).And.OnlyHaveUniqueItems(t => t.FeatureId);
    }

    [Fact]
    public async Task SEL_B11_PreviewAndApply_NameTheSameTargetIds_SoARetryCannotCreateASecondTargetForAFeature()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(2));
        await fixture.SeedOrderAsync();

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);
        await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        fixture.Store.Snapshot().Select(t => t.Id).Should().BeEquivalentTo(preview.FirstPage.Items.Select(t => t.Id));
        OrderTargetSelectionService.TargetId(OrderId, "p-001").Should().HaveLength(36)
            .And.Be(OrderTargetSelectionService.TargetId(OrderId, "p-001"))
            .And.NotBe(OrderTargetSelectionService.TargetId("ord-2", "p-001"));
    }

    // ── SEL-B12: identifier diagnostics ───────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_B12_IdentifierList_ReportsValuesNotFoundAndValuesMatchingSeveralFeatures(bool apply)
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("p-1", "P-100"), Pole("p-2", "P-200"), Pole("p-3", "P-200")]);
        await fixture.SeedOrderAsync();
        var request = IdentifierRequest(attributeKey: null, " P-100 ", "P-200", "P-300", "P-100", "  ", "P-400");

        IReadOnlyList<string> notFound, ambiguous;
        if (apply)
        {
            var result = await fixture.Sut.ApplyAsync(request, Admin, CancellationToken.None);
            (notFound, ambiguous) = (result.NotFoundValues, result.AmbiguousValues);
            result.Added.Should().Be(3);
        }
        else
        {
            var preview = await fixture.Sut.PreviewAsync(request, Admin, CancellationToken.None);
            (notFound, ambiguous) = (preview.NotFoundValues, preview.AmbiguousValues);
        }

        notFound.Should().Equal("P-300", "P-400");
        ambiguous.Should().Equal("P-200");
    }

    [Fact]
    public async Task SEL_B12_IdentifierListOnTheReservedIdKey_ComparesAgainstFeatureIds()
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("p-1"), Pole("p-2")]);
        await fixture.SeedOrderAsync();

        var preview = await fixture.Sut.PreviewAsync(
            IdentifierRequest(IdentifierListSelectionStrategy.FeatureIdKey, "p-1", "p-2", "p-9"), Admin, CancellationToken.None);

        preview.NotFoundValues.Should().Equal("p-9");
        preview.AmbiguousValues.Should().BeEmpty();
    }

    [Fact]
    public async Task SEL_B12_OtherStrategies_ReportNoIdentifierDiagnostics()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(2));
        await fixture.SeedOrderAsync();

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.NotFoundValues.Should().BeEmpty();
        preview.AmbiguousValues.Should().BeEmpty();
    }

    // ── SEL-B13 / SEL-B14: alias and position ─────────────────────────────────

    [Fact]
    public async Task SEL_B13_Alias_IsThePolicysAliasAttributeOfTheFeature_OrNullWhenTheFeatureHasNone()
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("p-1", "P-0001"), Pole("p-2", plate: null)]);
        await fixture.SeedOrderAsync();

        await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        fixture.Stored("p-1").Alias.Should().Be("P-0001");
        fixture.Stored("p-2").Alias.Should().BeNull();
    }

    [Fact]
    public async Task SEL_B13_Alias_IsNullWhenThePolicyNamesNoAliasAttribute_AndIsCutToWhatTheStoreAccepts()
    {
        using var withoutKey = new SelectionServiceFixture(Policy(aliasAttributeKey: null), answer: [Pole("p-1", "P-0001")]);
        await withoutKey.SeedOrderAsync();
        using var longAlias = new SelectionServiceFixture(answer: [Pole("p-1", new string('x', 300))]);
        await longAlias.SeedOrderAsync();

        await withoutKey.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);
        await longAlias.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        withoutKey.Store.Snapshot().Single().Alias.Should().BeNull();
        longAlias.Store.Snapshot().Single().Alias.Should().HaveLength(OrderTargetSelectionService.MaxAliasLength);
    }

    [Fact]
    public async Task SEL_B14_Position_IsTheFeaturesCentroid_AndNullForAFeatureWithoutGeometry_WhichTheExtentIgnores()
    {
        var line = new GeoFeature
        {
            Id = "line-1",
            Geometry = new GeoLineString([(-40, -10), (-38, -12)]),
            Properties = new GeoFeatureProperties { AssetTypeId = PoleType, OrganizationId = OrgA },
        };
        using var fixture = new SelectionServiceFixture(answer: [line, Pole("p-1"), Pole("p-2", withGeometry: false)]);
        await fixture.SeedOrderAsync();

        await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        fixture.Stored("line-1").Should().BeEquivalentTo(new { Longitude = -39.0, Latitude = -11.0 });
        fixture.Stored("p-1").Should().BeEquivalentTo(new { Longitude = -38.5, Latitude = -12.9 });
        fixture.Stored("p-2").Should().BeEquivalentTo(new { Longitude = (double?)null, Latitude = (double?)null });
        (await fixture.Store.GetExtentAsync(OrderId, CancellationToken.None))
            .Should().Be(new OrderTargetExtent(-39.0, -12.9, -38.5, -11.0));
    }

    // ── SEL-B15: cancellation ─────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_B15_AlreadyCancelledToken_ThrowsOperationCanceledException_AndLeavesNoSideEffects(bool apply)
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(3));
        var order = await fixture.SeedOrderAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = apply
            ? () => fixture.Sut.ApplyAsync(FenceRequest(), Admin, cancellation.Token)
            : () => fixture.Sut.PreviewAsync(FenceRequest(), Admin, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.Snapshot().Should().BeEmpty();
        order.SelectionSpec.Should().BeNull();
        order.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SEL_B15_Apply_CancelledAfterTheSelectionRanButBeforeTheWrite_LeavesNoSideEffects()
    {
        using var fixture = new SelectionServiceFixture();
        var order = await fixture.SeedOrderAsync();
        using var cancellation = new CancellationTokenSource();
        var reads = new CancelAfterReadsTargetReader(fixture.Store, cancellation, cancelAfterReads: 2);
        var writes = new CountingTargetWriter(fixture.Store);
        var registry = new FeatureSelectionRegistry(fixture.Clock, "no-such-plugins-directory", typeof(FeatureSelectionRegistry).Assembly);
        using (registry)
        {
            var selector = new BoundedFeatureSelector(registry, new FixedAnswerSpatialQuery(Poles(3)), assets: null, fixture.Clock);
            var sut = new OrderTargetSelectionService(
                fixture.Orders, fixture.OrderTypes, fixture.Rules, selector, reads, writes, fixture.Options, fixture.Clock);

            var act = () => sut.ApplyAsync(FenceRequest(), Admin, cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        reads.Reads.Should().Be(2, "the held-feature page and the conflict lookup both ran before the caller gave up");
        writes.Calls.Should().Be(0);
        fixture.Store.Snapshot().Should().BeEmpty();
        order.SelectionSpec.Should().BeNull();
        order.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SEL_B15_TheCallersToken_ReachesTheSpatialQuery()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(1));
        await fixture.SeedOrderAsync();
        using var cancellation = new CancellationTokenSource();

        await fixture.Sut.PreviewAsync(FenceRequest(), Admin, cancellation.Token);

        fixture.Query.Token.Should().Be(cancellation.Token);
    }

    // ── Clock ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_TakesAddedAtTheSpecAndTheActionLogTimeFromTheInjectedClock()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(2));
        var order = await fixture.SeedOrderAsync();

        await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        await fixture.Sut.ApplyAsync(FenceRequest(parameters: Fence()), Admin, CancellationToken.None);

        fixture.Store.Snapshot().Should().OnlyContain(t => t.AddedAt == Now.UtcDateTime && t.AddedAt.Kind == DateTimeKind.Utc);
        fixture.Store.Snapshot().Select(t => (t.AddedBy, t.Status, t.Version)).Distinct()
            .Should().Equal((Admin.UserId, OrderTargetStatus.Pending, 0));
        order.ActionLog.Select(e => e.PerformedAt).Should().Equal(Now.UtcDateTime, Now.UtcDateTime.AddHours(1));
        order.SelectionSpec!.ExecutedAt.Should().Be(Now.UtcDateTime.AddHours(1));
        order.UpdatedAt.Should().Be(Now.UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task Apply_StoresTargetsCarryingTheOrdersIdAndOrganization_AndTheFeaturesAssetType()
    {
        using var fixture = new SelectionServiceFixture(Policy(assetTypeIds: ["pole", "tower"]), answer: [Pole("p-1"), Pole("t-1", assetTypeId: "tower")]);
        await fixture.SeedOrderAsync();

        await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        fixture.Store.Snapshot().Select(t => (t.OrderId, t.OrganizationId)).Distinct().Should().Equal((OrderId, OrgA));
        fixture.Store.Snapshot().Select(t => (t.FeatureId, t.AssetTypeId)).Should().BeEquivalentTo([("p-1", "pole"), ("t-1", "tower")]);
    }

    // ── Doubles local to these tests ──────────────────────────────────────────

    private sealed class FixedAnswerSpatialQuery(params GeoFeature[] answer) : IAssetSpatialQuery
    {
        public Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GeoFeature>>(answer);

        public Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
            string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GeoFeature>>(answer);
    }

    /// <summary>Lets the first reads through, then cancels: the caller gives up mid-flight.</summary>
    private sealed class CancelAfterReadsTargetReader(
        IServiceOrderTargetReader inner, CancellationTokenSource cancellation, int cancelAfterReads) : IServiceOrderTargetReader
    {
        public int Reads { get; private set; }

        private async Task<T> ReadAsync<T>(Func<Task<T>> read)
        {
            var result = await read();
            if (++Reads == cancelAfterReads) await cancellation.CancelAsync();
            return result;
        }

        public Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct) =>
            ReadAsync(() => inner.GetPageAsync(query, ct));

        public Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct) =>
            ReadAsync(() => inner.GetByIdAsync(orderId, targetId, ct));

        public Task<int> CountAsync(string orderId, CancellationToken ct) => ReadAsync(() => inner.CountAsync(orderId, ct));

        public Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(
            IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) =>
            ReadAsync(() => inner.FindOpenUsagesAsync(featureIds, excludingOrderId, ct));

        public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct) =>
            ReadAsync(() => inner.GetExtentAsync(orderId, ct));
    }

    private sealed class CountingTargetWriter(IServiceOrderTargetWriter inner) : IServiceOrderTargetWriter
    {
        public int Calls { get; private set; }

        public Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct)
        {
            Calls++;
            return inner.AddRangeAsync(orderId, targets, ct);
        }

        public Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct)
        {
            Calls++;
            return inner.UpdateAsync(target, ct);
        }

        public Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct)
        {
            Calls++;
            return inner.RemoveAsync(orderId, targetIds, ct);
        }
    }
}
