using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary><see cref="IServiceOrderTargetReader"/>, and cancellation of every member of both ports.</summary>
public abstract partial class ServiceOrderTargetStoreContractTests
{
    // ── GetPageAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPageAsync_OrderWithoutTargetsOrUnknownOrder_ReturnsAnEmptyPageNotAnError()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");

        var empty = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None);
        var unknown = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-missing"), CancellationToken.None);

        empty.Items.Should().BeEmpty();
        empty.TotalCount.Should().Be(0);
        unknown.Items.Should().BeEmpty();
        unknown.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPageAsync_ListsOnlyTheTargetsOfTheOrderAsked()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await SeedOrderAsync(harness, "ord-2");
        await harness.Writer.AddRangeAsync("ord-1", [Target(harness, "ord-1", "feat-1").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-2", [Target(harness, "ord-2", "feat-1").Build(), Target(harness, "ord-2", "feat-2").Build()], CancellationToken.None);

        var page = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None);

        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.OrderId.Should().Be("ord-1");
    }

    [Fact]
    public async Task GetPageAsync_PagesThroughEveryTargetOnce_AndAPagePastTheEndIsEmptyWithTheTrueTotal()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            Enumerable.Range(1, 5).Select(i => Target(harness, "ord-1", $"feat-{i}").Build()).ToList(),
            CancellationToken.None);

        var pages = new List<PagedResult<ServiceOrderTarget>>();
        for (var page = 1; page <= 4; page++)
            pages.Add(await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", page, pageSize: 2), CancellationToken.None));

        pages.Select(p => p.TotalCount).Should().AllBeEquivalentTo(5);
        pages[0].Items.Select(t => t.FeatureId).Should().Equal("feat-1", "feat-2");
        pages[1].Items.Select(t => t.FeatureId).Should().Equal("feat-3", "feat-4");
        pages[2].Items.Select(t => t.FeatureId).Should().Equal("feat-5");
        pages[3].Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("featureId")]
    [InlineData("no-such-key")]
    [InlineData("Alias")]
    public async Task GetPageAsync_NoOrUnrecognizedSortKey_SortsByFeatureId(string? sortBy)
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "feat-c").WithId("tgt-1").WithAlias("A").Build(),
                Target(harness, "ord-1", "feat-a").WithId("tgt-2").WithAlias("C").Build(),
                Target(harness, "ord-1", "feat-b").WithId("tgt-3").WithAlias("B").Build(),
            ],
            CancellationToken.None);

        var ascending = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", sortBy: sortBy), CancellationToken.None);
        var descending = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", sortBy: sortBy, descending: true), CancellationToken.None);

        ascending.Items.Select(t => t.FeatureId).Should().Equal("feat-a", "feat-b", "feat-c");
        descending.Items.Select(t => t.FeatureId).Should().Equal("feat-c", "feat-b", "feat-a");
    }

    /// <summary>
    /// Five targets whose sort keys tie in pairs; the expectation lists target ids ascending,
    /// and descending must reverse the key while keeping each tie in ascending id order.
    /// </summary>
    [Theory]
    [InlineData(OrderTargetSortKeys.Status, "t2,t4,t1,t3,t5", "t5,t1,t3,t2,t4")]
    [InlineData(OrderTargetSortKeys.ConflictLevel, "t1,t5,t3,t2,t4", "t2,t4,t3,t1,t5")]
    [InlineData(OrderTargetSortKeys.AddedAt, "t3,t4,t1,t2,t5", "t5,t1,t2,t3,t4")]
    [InlineData(OrderTargetSortKeys.Alias, "t2,t5,t1,t3,t4", "t3,t4,t1,t2,t5")]
    public async Task GetPageAsync_EachSortKey_OrdersByItInTheAskedDirectionThenByIdAscending(string sortBy, string ascendingIds, string descendingIds)
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var day = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "f1").WithId("t1").WithStatus(OrderTargetStatus.Pending).WithConflictLevel(OrderTargetConflictLevel.None).WithAddedAt(day.AddHours(2)).WithAlias("P-2").Build(),
                Target(harness, "ord-1", "f2").WithId("t2").WithStatus(OrderTargetStatus.Done).WithConflictLevel(OrderTargetConflictLevel.Blocked).WithAddedAt(day.AddHours(2)).WithAlias("P-1").Build(),
                Target(harness, "ord-1", "f3").WithId("t3").WithStatus(OrderTargetStatus.Pending).WithConflictLevel(OrderTargetConflictLevel.Warning).WithAddedAt(day.AddHours(1)).WithAlias(null).Build(),
                Target(harness, "ord-1", "f4").WithId("t4").WithStatus(OrderTargetStatus.Done).WithConflictLevel(OrderTargetConflictLevel.Blocked).WithAddedAt(day.AddHours(1)).WithAlias(null).Build(),
                Target(harness, "ord-1", "f5").WithId("t5").WithStatus(OrderTargetStatus.Skipped).WithConflictLevel(OrderTargetConflictLevel.None).WithAddedAt(day.AddHours(3)).WithAlias("P-1").Build(),
            ],
            CancellationToken.None);

        var ascending = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", sortBy: sortBy), CancellationToken.None);
        var descending = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", sortBy: sortBy, descending: true), CancellationToken.None);

        string.Join(',', ascending.Items.Select(t => t.Id)).Should().Be(ascendingIds);
        string.Join(',', descending.Items.Select(t => t.Id)).Should().Be(descendingIds);
    }

    [Fact]
    public async Task GetPageAsync_SortedPagesOfTiedKeys_NeverOverlapOrSkip()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            Enumerable.Range(0, 11)
                .Select(i => Target(harness, "ord-1", $"feat-{i:D2}").WithId($"tgt-{10 - i:D2}").WithStatus(i % 2 == 0 ? OrderTargetStatus.Pending : OrderTargetStatus.Done).Build())
                .ToList(),
            CancellationToken.None);

        var seen = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await harness.Reader.GetPageAsync(
                new OrderTargetQuery("ord-1", page, pageSize: 3, sortBy: OrderTargetSortKeys.Status, descending: true), CancellationToken.None);
            seen.AddRange(result.Items.Select(t => t.Id));
        }

        seen.Should().OnlyHaveUniqueItems().And.HaveCount(11);
    }

    [Fact]
    public async Task GetPageAsync_TextKeysAndTheIdTiebreak_CompareOrdinally_UppercaseBeforeLowercase()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "feat-a").WithId("tgt-b").WithAlias("same").Build(),
                Target(harness, "ord-1", "feat-B").WithId("tgt-C").WithAlias("same").Build(),
                Target(harness, "ord-1", "feat-C").WithId("tgt-a").WithAlias("same").Build(),
            ],
            CancellationToken.None);

        var byFeature = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None);
        var byTiedAlias = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", sortBy: OrderTargetSortKeys.Alias), CancellationToken.None);

        // A linguistic collation would give feat-a, feat-B, feat-C and tgt-a, tgt-b, tgt-C.
        byFeature.Items.Select(t => t.FeatureId).Should().Equal("feat-B", "feat-C", "feat-a");
        byTiedAlias.Items.Select(t => t.Id).Should().Equal("tgt-C", "tgt-a", "tgt-b");
    }

    [Fact]
    public async Task GetPageAsync_StatusAndConflictLevelFilters_ApplyAloneAndTogether_AndTotalCountCountsTheMatches()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "f1").WithStatus(OrderTargetStatus.Pending).WithConflictLevel(OrderTargetConflictLevel.None).Build(),
                Target(harness, "ord-1", "f2").WithStatus(OrderTargetStatus.Pending).WithConflictLevel(OrderTargetConflictLevel.Blocked).Build(),
                Target(harness, "ord-1", "f3").WithStatus(OrderTargetStatus.Done).WithConflictLevel(OrderTargetConflictLevel.Blocked).Build(),
                Target(harness, "ord-1", "f4").WithStatus(OrderTargetStatus.Pending).WithConflictLevel(OrderTargetConflictLevel.Blocked).Build(),
            ],
            CancellationToken.None);

        var byStatus = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", pageSize: 2, status: OrderTargetStatus.Pending), CancellationToken.None);
        var byLevel = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", conflictLevel: OrderTargetConflictLevel.Blocked), CancellationToken.None);
        var byNoneLevel = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", conflictLevel: OrderTargetConflictLevel.None), CancellationToken.None);
        var byBoth = await harness.Reader.GetPageAsync(
            new OrderTargetQuery("ord-1", status: OrderTargetStatus.Pending, conflictLevel: OrderTargetConflictLevel.Blocked), CancellationToken.None);
        var nothing = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", status: OrderTargetStatus.Skipped), CancellationToken.None);
        var wrongCase = await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1", status: "pending"), CancellationToken.None);

        byStatus.TotalCount.Should().Be(3, "the total counts every match, not just the two on the page");
        byStatus.Items.Select(t => t.FeatureId).Should().Equal("f1", "f2");
        byLevel.Items.Select(t => t.FeatureId).Should().Equal("f2", "f3", "f4");
        byNoneLevel.Items.Select(t => t.FeatureId).Should().Equal("f1");
        byBoth.Items.Select(t => t.FeatureId).Should().Equal("f2", "f4");
        byBoth.TotalCount.Should().Be(2);
        nothing.Items.Should().BeEmpty();
        nothing.TotalCount.Should().Be(0);
        wrongCase.TotalCount.Should().Be(0, "the status filter is case-sensitive");
    }

    [Fact]
    public async Task GetPageAsync_NullQuery_ThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Reader.GetPageAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── CountAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountAsync_CountsTargetsInAnyStatus_IncludingReleasedOnes_AndZeroForAnUnknownOrder()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await SeedOrderAsync(harness, "ord-2");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "f1").WithStatus(OrderTargetStatus.Pending).Build(),
                Target(harness, "ord-1", "f2").WithStatus(OrderTargetStatus.Done).Build(),
                Target(harness, "ord-1", "f3").WithStatus(OrderTargetStatus.Skipped).Build(),
            ],
            CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-2", [Target(harness, "ord-2", "f1").Build()], CancellationToken.None);

        var open = await harness.Reader.CountAsync("ord-1", CancellationToken.None);
        await MoveOrderAsync(harness, "ord-1", ServiceOrderStatus.Cancelled);
        var released = await harness.Reader.CountAsync("ord-1", CancellationToken.None);

        open.Should().Be(3);
        released.Should().Be(3, "closing an order releases its targets but does not remove them");
        (await harness.Reader.CountAsync("ord-missing", CancellationToken.None)).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CountAsync_BlankOrderId_ThrowsArgumentException(string orderId)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Reader.CountAsync(orderId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── FindOpenUsagesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FindOpenUsagesAsync_ReturnsOneUsagePerFeatureAndHoldingOrder_WithThatOrdersTypeStatusParentAndExclusivity()
    {
        await using var harness = await CreateHarnessAsync();
        harness.OrderTypes.Register(TypeWithExclusivity("survey", OrderTargetExclusivity.WarnOnOverlap));
        harness.OrderTypes.Register(TypeWithExclusivity("works", OrderTargetExclusivity.BlockOnOverlap));
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-root", orderTypeId: "survey");
        await SeedOrderAsync(harness, "ord-child", orderTypeId: "works", parentOrderId: "ord-root");
        await MoveOrderAsync(harness, "ord-child", ServiceOrderStatus.Pending);
        await harness.Writer.AddRangeAsync("ord-mine", [Target(harness, "ord-mine", "feat-1").Build(), Target(harness, "ord-mine", "feat-2").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-root", [Target(harness, "ord-root", "feat-1").Build(), Target(harness, "ord-root", "feat-9").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-child", [Target(harness, "ord-child", "feat-1").Build(), Target(harness, "ord-child", "feat-2").Build()], CancellationToken.None);

        var usages = await harness.Reader.FindOpenUsagesAsync(["feat-1", "feat-2", "feat-3"], "ord-mine", CancellationToken.None);

        usages.Should().BeEquivalentTo(
        [
            new OrderTargetUsage("feat-1", "ord-root", "survey", ServiceOrderStatus.Draft, null, OrderTargetExclusivity.WarnOnOverlap),
            new OrderTargetUsage("feat-1", "ord-child", "works", ServiceOrderStatus.Pending, "ord-root", OrderTargetExclusivity.BlockOnOverlap),
            new OrderTargetUsage("feat-2", "ord-child", "works", ServiceOrderStatus.Pending, "ord-root", OrderTargetExclusivity.BlockOnOverlap),
        ]);
    }

    [Fact]
    public async Task FindOpenUsagesAsync_SameFamilyOrders_AreReturned_FilteringThemIsTheEvaluatorsJob()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-parent");
        await SeedOrderAsync(harness, "ord-child", parentOrderId: "ord-parent");
        await harness.Writer.AddRangeAsync("ord-parent", [Target(harness, "ord-parent", "feat-1").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-child", [Target(harness, "ord-child", "feat-1").Build()], CancellationToken.None);

        var seenByChild = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-child", CancellationToken.None);
        var seenByParent = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-parent", CancellationToken.None);

        seenByChild.Should().ContainSingle().Which.OrderId.Should().Be("ord-parent");
        seenByParent.Should().ContainSingle().Which.Should().Be(
            new OrderTargetUsage("feat-1", "ord-child", "inspection", ServiceOrderStatus.Draft, "ord-parent", OrderTargetExclusivity.None));
    }

    [Fact]
    public async Task FindOpenUsagesAsync_OrderTypeUnknownOrWithoutTargetPolicy_ReportsExclusivityNone()
    {
        await using var harness = await CreateHarnessAsync();
        harness.OrderTypes.Register(new OrderType { Id = "plain", DisplayName = "Plain" });
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-plain", orderTypeId: "plain");
        await SeedOrderAsync(harness, "ord-unregistered", orderTypeId: "never-registered");
        await harness.Writer.AddRangeAsync("ord-plain", [Target(harness, "ord-plain", "feat-1").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-unregistered", [Target(harness, "ord-unregistered", "feat-1").Build()], CancellationToken.None);

        var usages = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None);

        usages.Should().HaveCount(2).And.OnlyContain(u => u.Exclusivity == OrderTargetExclusivity.None);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed)]
    public async Task FindOpenUsagesAsync_TargetsOfAnOrderThatReachedATerminalState_AreReleasedAndNotReturned(params string[] pathToTerminalState)
    {
        ArgumentNullException.ThrowIfNull(pathToTerminalState);
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-closing");
        await SeedOrderAsync(harness, "ord-open");
        await harness.Writer.AddRangeAsync("ord-closing", [Target(harness, "ord-closing", "feat-1").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-open", [Target(harness, "ord-open", "feat-1").Build()], CancellationToken.None);
        var before = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None);

        await MoveOrderAsync(harness, "ord-closing", pathToTerminalState);

        var after = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None);
        before.Select(u => u.OrderId).Should().BeEquivalentTo("ord-closing", "ord-open");
        after.Select(u => u.OrderId).Should().Equal("ord-open");
    }

    [Fact]
    public async Task FindOpenUsagesAsync_OrderMovedToANonTerminalState_StillHoldsItsTargets()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-busy");
        await harness.Writer.AddRangeAsync("ord-busy", [Target(harness, "ord-busy", "feat-1").Build()], CancellationToken.None);

        await MoveOrderAsync(harness, "ord-busy", ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold);

        var usages = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None);
        usages.Should().ContainSingle().Which.OrderStatus.Should().Be(ServiceOrderStatus.OnHold);
    }

    [Fact]
    public async Task FindOpenUsagesAsync_TerminalStateOfACustomOrderTypeGraph_ReleasesToo()
    {
        await using var harness = await CreateHarnessAsync();
        harness.OrderTypes.Register(new OrderType
        {
            Id = "two-step",
            DisplayName = "Two step",
            States = [new WorkflowState("Open", "Open"), new WorkflowState("Closed", "Closed")],
            Transitions = [new WorkflowTransition("Open", "Closed", OrderActionType.Complete)],
            InitialStateKey = "Open",
        });
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-custom", orderTypeId: "two-step", status: "Open");
        await harness.Writer.AddRangeAsync("ord-custom", [Target(harness, "ord-custom", "feat-1").Build()], CancellationToken.None);
        var before = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None);

        await MoveOrderAsync(harness, "ord-custom", "Closed");

        before.Should().ContainSingle();
        (await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task FindOpenUsagesAsync_TargetAddedToAnOrderThatIsAlreadyClosed_IsNeverAnOpenUsage()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-closed");
        await MoveOrderAsync(harness, "ord-closed", ServiceOrderStatus.Cancelled);

        var inserted = await harness.Writer.AddRangeAsync("ord-closed", [Target(harness, "ord-closed", "feat-1").Build()], CancellationToken.None);

        inserted.Should().Be(1);
        (await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task FindOpenUsagesAsync_ThePerTargetStatus_PlaysNoPart()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-other");
        await harness.Writer.AddRangeAsync(
            "ord-other",
            OrderTargetStatus.All.Select(status => Target(harness, "ord-other", $"feat-{status}").WithStatus(status).Build()).ToList(),
            CancellationToken.None);

        var usages = await harness.Reader.FindOpenUsagesAsync(
            OrderTargetStatus.All.Select(status => $"feat-{status}").ToList(), "ord-mine", CancellationToken.None);

        usages.Should().HaveCount(OrderTargetStatus.All.Count);
    }

    [Fact]
    public async Task FindOpenUsagesAsync_EmptyFeatureIds_ReturnsEmpty_AndRepeatedIdsCountOnce()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-other");
        await harness.Writer.AddRangeAsync("ord-other", [Target(harness, "ord-other", "feat-1").Build()], CancellationToken.None);

        var none = await harness.Reader.FindOpenUsagesAsync([], "ord-mine", CancellationToken.None);
        var repeated = await harness.Reader.FindOpenUsagesAsync(["feat-1", "feat-1", "feat-1"], "ord-mine", CancellationToken.None);

        none.Should().BeEmpty();
        repeated.Should().ContainSingle();
    }

    [Fact]
    public async Task FindOpenUsagesAsync_ThousandsOfFeatureIds_FindsUsagesWhereverTheyFallInTheList()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-other");
        var asked = Enumerable.Range(0, 2500).Select(i => $"feat-{i:D5}").ToList();
        string[] held = ["feat-00000", "feat-00999", "feat-01000", "feat-01777", "feat-02499"];
        await harness.Writer.AddRangeAsync(
            "ord-other", held.Select(featureId => Target(harness, "ord-other", featureId).Build()).ToList(), CancellationToken.None);

        var usages = await harness.Reader.FindOpenUsagesAsync(asked, "ord-mine", CancellationToken.None);

        usages.Select(u => u.FeatureId).Should().BeEquivalentTo(held);
    }

    [Fact]
    public async Task FindOpenUsagesAsync_OrdersOfAnotherOrganization_AreNeverReturned()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-mine");
        await SeedOrderAsync(harness, "ord-same-org");
        await SeedOrderAsync(harness, "ord-foreign", organizationId: OtherOrganizationId);
        await harness.Writer.AddRangeAsync("ord-same-org", [Target(harness, "ord-same-org", "feat-1").Build()], CancellationToken.None);
        await harness.Writer.AddRangeAsync(
            "ord-foreign", [Target(harness, "ord-foreign", "feat-1").WithOrganizationId(OtherOrganizationId).Build()], CancellationToken.None);

        var forMyOrder = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-mine", CancellationToken.None);
        var beforeMyOrderExists = await harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-not-created-yet", CancellationToken.None);

        forMyOrder.Select(u => u.OrderId).Should().Equal("ord-same-org");
        beforeMyOrderExists.Select(u => u.OrderId).Should().Equal(["ord-same-org"], "with no order to scope by, the caller's own organization is the scope");
    }

    [Fact]
    public async Task FindOpenUsagesAsync_NullFeatureIds_ThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Reader.FindOpenUsagesAsync(null!, "ord-1", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindOpenUsagesAsync_BlankExcludingOrderId_ThrowsArgumentException(string excludingOrderId)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Reader.FindOpenUsagesAsync(["feat-1"], excludingOrderId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── GetExtentAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExtentAsync_NoPositionedTarget_ReturnsNull()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-empty");
        await SeedOrderAsync(harness, "ord-unpositioned");
        await harness.Writer.AddRangeAsync(
            "ord-unpositioned", [Target(harness, "ord-unpositioned", "feat-1").WithoutPosition().Build()], CancellationToken.None);

        (await harness.Reader.GetExtentAsync("ord-empty", CancellationToken.None)).Should().BeNull();
        (await harness.Reader.GetExtentAsync("ord-unpositioned", CancellationToken.None)).Should().BeNull();
        (await harness.Reader.GetExtentAsync("ord-missing", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetExtentAsync_OnePositionedTarget_ReturnsADegenerateBox()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync("ord-1", [Target(harness, "ord-1", "feat-1").WithPosition(-38.5, -12.97).Build()], CancellationToken.None);

        var extent = await harness.Reader.GetExtentAsync("ord-1", CancellationToken.None);

        extent.Should().Be(new OrderTargetExtent(-38.5, -12.97, -38.5, -12.97));
    }

    [Fact]
    public async Task GetExtentAsync_ManyTargets_ReturnsMinAndMaxOverThePositionedOnesOfThatOrderOnly()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await SeedOrderAsync(harness, "ord-2");
        await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "feat-1").WithPosition(-38.50, -12.90).Build(),
                Target(harness, "ord-1", "feat-2").WithPosition(-38.70, -12.95).Build(),
                Target(harness, "ord-1", "feat-3").WithPosition(-38.40, -13.10).Build(),
                Target(harness, "ord-1", "feat-4").WithoutPosition().Build(),
                // Half a position is no position: this longitude must not widen the box.
                Target(harness, "ord-1", "feat-5").Build() with { Longitude = 120, Latitude = null },
                Target(harness, "ord-1", "feat-6").Build() with { Longitude = null, Latitude = 80 },
            ],
            CancellationToken.None);
        await harness.Writer.AddRangeAsync("ord-2", [Target(harness, "ord-2", "feat-1").WithPosition(0, 0).Build()], CancellationToken.None);

        var extent = await harness.Reader.GetExtentAsync("ord-1", CancellationToken.None);

        extent.Should().Be(new OrderTargetExtent(-38.70, -13.10, -38.40, -12.90));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetExtentAsync_BlankOrderId_ThrowsArgumentException(string orderId)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Reader.GetExtentAsync(orderId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryMember_WithAnAlreadyCancelledToken_ThrowsOperationCanceledExceptionAndChangesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var stored = Target(harness, "ord-1", "feat-1").WithId("tgt-1").Build();
        await harness.Writer.AddRangeAsync("ord-1", [stored], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = cancellation.Token;

        Func<Task>[] calls =
        [
            () => harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1"), cancelled),
            () => harness.Reader.CountAsync("ord-1", cancelled),
            () => harness.Reader.FindOpenUsagesAsync(["feat-1"], "ord-2", cancelled),
            () => harness.Reader.GetExtentAsync("ord-1", cancelled),
            () => harness.Writer.AddRangeAsync("ord-1", [Target(harness, "ord-1", "feat-2").Build()], cancelled),
            () => harness.Writer.UpdateAsync(stored with { Note = "never written" }, cancelled),
            () => harness.Writer.RemoveAsync("ord-1", ["tgt-1"], cancelled),
        ];

        foreach (var call in calls)
            await call.Should().ThrowAsync<OperationCanceledException>();

        (await AllTargetsAsync(harness, "ord-1")).Should().BeEquivalentTo([stored]);
    }

    private static OrderType TypeWithExclusivity(string id, OrderTargetExclusivity exclusivity) => new()
    {
        Id = id,
        DisplayName = id,
        TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = exclusivity },
    };
}
