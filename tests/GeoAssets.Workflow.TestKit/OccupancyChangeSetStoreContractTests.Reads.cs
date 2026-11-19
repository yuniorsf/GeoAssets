using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <content>Reading, the target/order life cycle, and tenancy.</content>
public abstract partial class OccupancyChangeSetStoreContractTests
{
    // ── GetStagedAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStagedAsync_NothingStaged_UnknownOrderOrTarget_OrTheTargetOfAnotherOrder_ReturnsNull()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-2", "pole-2");
        var staged = await StageAsync(harness, "ord-2", "pole-2");

        (await harness.Reader.GetStagedAsync("ord-1", TargetId("ord-1", "pole-1"), CancellationToken.None)).Should().BeNull();
        (await harness.Reader.GetStagedAsync("ord-missing", staged.TargetId, CancellationToken.None)).Should().BeNull();
        (await harness.Reader.GetStagedAsync("ord-2", "tgt-missing", CancellationToken.None)).Should().BeNull();
        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeNull("that target belongs to ord-2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetStagedAsync_BlankId_ThrowsArgumentException(string? blank)
    {
        await using var harness = await CreateHarnessAsync();

        await ((Func<Task>)(() => harness.Reader.GetStagedAsync(blank!, "tgt-1", CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => harness.Reader.GetStagedAsync("ord-1", blank!, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Reading_WithAnAlreadyCancelledToken_Throws()
    {
        await using var harness = await CreateHarnessAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await ((Func<Task>)(() => harness.Reader.GetStagedAsync("ord-1", "tgt-1", cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1"), cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
    }

    // ── GetPageAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPageAsync_OrdersByPoleFeatureIdOrdinally_UppercaseBeforeLowercase()
    {
        await using var harness = await CreateHarnessAsync();
        string[] poles = ["pole-b", "Pole-C", "pole-a", "POLE-d", "pole-10", "pole-9"];
        foreach (var pole in poles)
        {
            await SeedTargetAsync(harness, "ord-1", pole);
            await StageAsync(harness, "ord-1", pole);
        }

        var page = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None);

        page.TotalCount.Should().Be(6);
        page.Items.Select(set => set.PoleFeatureId).Should().Equal(poles.Order(StringComparer.Ordinal));
        page.Items.Select(set => set.PoleFeatureId).Should().Equal("POLE-d", "Pole-C", "pole-10", "pole-9", "pole-a", "pole-b");
    }

    [Fact]
    public async Task GetPageAsync_SetsOfOnePole_ComeInTheOrdinalOrderOfTheirIdsCanonicalText()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var ids = new List<Guid>();
        for (var round = 0; round < 6; round++)
        {
            var staged = await StageAsync(harness, "ord-1", "pole-1");
            ids.Add(staged.Id);
            await harness.Writer.DiscardAsync("ord-1", staged.TargetId, CancellationToken.None);
        }

        var page = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None);

        page.Items.Select(set => set.Id).Should().Equal(ids.OrderBy(id => id.ToString("D"), StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetPageAsync_PagesTheMatches_CountsAllOfThem_AndAPagePastTheEndIsEmptyWithTheSameTotal()
    {
        await using var harness = await CreateHarnessAsync();
        for (var pole = 1; pole <= 7; pole++)
        {
            await SeedTargetAsync(harness, "ord-1", $"pole-{pole}");
            await StageAsync(harness, "ord-1", $"pole-{pole}");
        }

        var first = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1", page: 1, pageSize: 3), CancellationToken.None);
        var third = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1", page: 3, pageSize: 3), CancellationToken.None);
        var beyond = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1", page: 4, pageSize: 3), CancellationToken.None);

        first.TotalCount.Should().Be(7);
        first.Items.Select(set => set.PoleFeatureId).Should().Equal("pole-1", "pole-2", "pole-3");
        third.TotalCount.Should().Be(7);
        third.Items.Select(set => set.PoleFeatureId).Should().Equal("pole-7");
        beyond.TotalCount.Should().Be(7);
        beyond.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAsync_StatusFilter_KeepsOnlyThatStatus_AndCountsOnlyThose()
    {
        await using var harness = await CreateHarnessAsync();
        foreach (var pole in new[] { "pole-1", "pole-2", "pole-3" })
        {
            await SeedTargetAsync(harness, "ord-1", pole);
            await StageAsync(harness, "ord-1", pole);
        }

        await harness.Writer.DiscardAsync("ord-1", TargetId("ord-1", "pole-2"), CancellationToken.None);

        var staged = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1", status: OccupancyChangeSetStatus.Staged), CancellationToken.None);
        var discarded = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1", status: OccupancyChangeSetStatus.Discarded), CancellationToken.None);
        var committed = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1", status: OccupancyChangeSetStatus.Committed), CancellationToken.None);
        var all = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None);

        staged.TotalCount.Should().Be(2);
        staged.Items.Select(set => set.PoleFeatureId).Should().Equal("pole-1", "pole-3");
        discarded.TotalCount.Should().Be(1);
        discarded.Items.Should().ContainSingle().Which.PoleFeatureId.Should().Be("pole-2");
        committed.TotalCount.Should().Be(0);
        committed.Items.Should().BeEmpty();
        all.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetPageAsync_ListsOnlyTheSetsOfThatOrder_AndAnUnknownOrderHasNone()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-2", "pole-1");
        var mine = await StageAsync(harness, "ord-1", "pole-1");
        await StageAsync(harness, "ord-2", "pole-1");

        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        var unknown = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-missing"), CancellationToken.None);
        unknown.TotalCount.Should().Be(0);
        unknown.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAsync_NullQuery_ThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Reader.GetPageAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── They follow their target and their order ──────────────────────────────

    [Fact]
    public async Task RemovingTheTarget_RemovesItsSetsInEveryStatus_AndLeavesTheOtherTargetsSets()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-1", "pole-2");
        var discarded = await StageAsync(harness, "ord-1", "pole-1");
        await harness.Writer.DiscardAsync("ord-1", discarded.TargetId, CancellationToken.None);
        await StageAsync(harness, "ord-1", "pole-1");
        var kept = await StageAsync(harness, "ord-1", "pole-2");

        await harness.Targets.RemoveAsync("ord-1", [TargetId("ord-1", "pole-1")], CancellationToken.None);

        (await harness.Reader.GetStagedAsync("ord-1", TargetId("ord-1", "pole-1"), CancellationToken.None)).Should().BeNull();
        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Id.Should().Be(kept.Id);
    }

    [Fact]
    public async Task DeletingTheOrder_RemovesAllItsSets_AndOnlyIts()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-2", "pole-1");
        await StageAsync(harness, "ord-1", "pole-1");
        var kept = await StageAsync(harness, "ord-2", "pole-1");

        await harness.Orders.DeleteAsync("ord-1");

        (await AllSetsAsync(harness, "ord-1")).Should().BeEmpty();
        (await harness.Reader.GetStagedAsync("ord-1", TargetId("ord-1", "pole-1"), CancellationToken.None)).Should().BeNull();
        (await AllSetsAsync(harness, "ord-2")).Should().ContainSingle().Which.Id.Should().Be(kept.Id);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.Completed)]
    public async Task ClosingTheOrder_DiscardsWhatIsStillStagedOnIt_AndLeavesAlreadyDiscardedSetsAndOtherOrdersAlone(string closedAs)
    {
        ArgumentNullException.ThrowIfNull(closedAs);
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-1", "pole-2");
        await SeedTargetAsync(harness, "ord-2", "pole-1");
        var earlier = await StageAsync(harness, "ord-1", "pole-2");
        await harness.Writer.DiscardAsync("ord-1", earlier.TargetId, CancellationToken.None);
        var earlierClosedAt = harness.Clock.GetUtcNow();
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        var other = await StageAsync(harness, "ord-2", "pole-1");
        harness.Clock.Advance(TimeSpan.FromHours(2));

        await MoveOrderAsync(harness, "ord-1", string.Equals(closedAs, ServiceOrderStatus.Completed, StringComparison.Ordinal)
            ? [ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed]
            : [ServiceOrderStatus.Cancelled]);

        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeNull();
        var sets = await AllSetsAsync(harness, "ord-1");
        sets.Should().HaveCount(2).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Discarded);
        sets.Single(set => set.Id == staged.Id).ClosedAt.Should().Be(harness.Clock.GetUtcNow(), "it was discarded when the order closed");
        sets.Single(set => set.Id == earlier.Id).ClosedAt.Should().Be(earlierClosedAt, "it had been discarded before");
        (await harness.Reader.GetStagedAsync("ord-2", other.TargetId, CancellationToken.None))!.Id.Should().Be(other.Id);
        (await harness.Writer.DiscardAllAsync("ord-1", CancellationToken.None)).Should().Be(0, "nothing is left staged on a closed order");
    }

    [Fact]
    public async Task MovingTheOrderBetweenOpenStates_LeavesItsStagedSetsStaged()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");

        await MoveOrderAsync(harness, "ord-1", ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold);

        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeEquivalentTo(staged);
    }

    // ── Owner only ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnotherOrganization_ReadsNoneOfTheOwnersSets_AndCanDiscardNoneOfThem()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        var outsider = harness.AsOrganization(OtherOrganizationId);

        (await outsider.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeNull();
        var page = await outsider.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None);
        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
        (await outsider.Writer.DiscardAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeFalse();
        (await outsider.Writer.DiscardAllAsync("ord-1", CancellationToken.None)).Should().Be(0);

        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeEquivalentTo(staged);
    }

    [Fact]
    public async Task AnotherOrganization_CannotStageOnTheOwnersTarget_NotEvenUnderItsOwnName()
    {
        await using var harness = await CreateHarnessAsync();
        var target = await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        var outsider = harness.AsOrganization(OtherOrganizationId);
        var asItself = OccupancyChangeSet.Stage(
            "ord-1", target.Id, "pole-1", OtherOrganizationId, [OccupancyChangeBuilder.Add(1)], null, "intruder", harness.Clock.GetUtcNow());
        var asTheOwner = NewSet(harness, "ord-1", "pole-1", stagedBy: "intruder");

        await ((Func<Task>)(() => outsider.Writer.StageAsync(asItself, CancellationToken.None)))
            .Should().ThrowAsync<KeyNotFoundException>("the target is not the outsider's");
        await ((Func<Task>)(() => outsider.Writer.StageAsync(asTheOwner, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentException>("the set names another organization than the store's");

        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Should().BeEquivalentTo(staged);
    }
}
