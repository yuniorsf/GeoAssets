using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// <see cref="IOccupancyChangeSetWriter.MarkCommittedAsync"/> (XD01-318): a staged set is
/// recorded as committed exactly once, a set that is no longer staged is left alone and reported
/// as such, and closing the order afterwards does not discard what was committed.
/// </summary>
public abstract partial class OccupancyChangeSetStoreContractTests
{
    [Fact]
    public async Task MarkCommittedAsync_AStagedSet_IsRecordedCommittedAtTheGivenInstant_AndIsNoLongerStaged()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        harness.Clock.Advance(TimeSpan.FromHours(3));
        var committedAt = harness.Clock.GetUtcNow();

        var marked = await harness.Writer.MarkCommittedAsync(staged.Commit(committedAt), CancellationToken.None);

        marked.Should().BeTrue();
        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeNull();
        var onFile = (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Subject;
        onFile.Should().BeEquivalentTo(staged, options => options.Excluding(set => set.Status).Excluding(set => set.ClosedAt));
        onFile.Status.Should().Be(OccupancyChangeSetStatus.Committed);
        onFile.ClosedAt.Should().Be(committedAt);
    }

    [Fact]
    public async Task MarkCommittedAsync_ASecondTime_ReturnsFalse_AndKeepsTheFirstClosingInstant()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        var firstAt = harness.Clock.GetUtcNow();
        await harness.Writer.MarkCommittedAsync(staged.Commit(firstAt), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(1));

        var again = await harness.Writer.MarkCommittedAsync(staged.Commit(harness.Clock.GetUtcNow()), CancellationToken.None);

        again.Should().BeFalse();
        (await AllSetsAsync(harness, "ord-1")).Single().ClosedAt.Should().Be(firstAt);
    }

    [Fact]
    public async Task MarkCommittedAsync_ASetReplacedByAnotherStaging_ReturnsFalse_AndLeavesTheNewSetStaged()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var replaced = await StageAsync(harness, "ord-1", "pole-1");
        var current = await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(slotIndex: 4));

        var marked = await harness.Writer.MarkCommittedAsync(replaced.Commit(harness.Clock.GetUtcNow()), CancellationToken.None);

        marked.Should().BeFalse();
        (await harness.Reader.GetStagedAsync("ord-1", current.TargetId, CancellationToken.None)).Should().BeEquivalentTo(current);
        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle();
    }

    [Fact]
    public async Task MarkCommittedAsync_ADiscardedOrUnknownSet_ReturnsFalse_AndChangesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        await harness.Writer.DiscardAsync("ord-1", staged.TargetId, CancellationToken.None);
        var unknown = NewSet(harness, "ord-1", "pole-1");

        (await harness.Writer.MarkCommittedAsync(staged.Commit(harness.Clock.GetUtcNow()), CancellationToken.None)).Should().BeFalse();
        (await harness.Writer.MarkCommittedAsync(unknown.Commit(harness.Clock.GetUtcNow()), CancellationToken.None)).Should().BeFalse();

        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Discarded);
    }

    [Fact]
    public async Task MarkCommittedAsync_ThenClosingTheOrder_LeavesTheCommittedSetCommitted_AndDiscardsOnlyWhatIsStillStaged()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-1", "pole-2");
        var committed = await StageAsync(harness, "ord-1", "pole-1");
        var leftStaged = await StageAsync(harness, "ord-1", "pole-2");
        var committedAt = harness.Clock.GetUtcNow();
        await harness.Writer.MarkCommittedAsync(committed.Commit(committedAt), CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        await MoveOrderAsync(harness, "ord-1", ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed);

        var sets = await AllSetsAsync(harness, "ord-1");
        sets.Single(set => set.Id == committed.Id).Should().Match<OccupancyChangeSet>(set =>
            set.Status == OccupancyChangeSetStatus.Committed && set.ClosedAt == committedAt);
        sets.Single(set => set.Id == leftStaged.Id).Status.Should().Be(OccupancyChangeSetStatus.Discarded);
    }

    [Fact]
    public async Task MarkCommittedAsync_ASetThatIsNotCommitted_OrOfAnotherOrganization_ThrowsArgumentException_AndNullThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        var outsider = harness.AsOrganization(OtherOrganizationId);

        await ((Func<Task>)(() => harness.Writer.MarkCommittedAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => harness.Writer.MarkCommittedAsync(staged, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentException>("only a committed set is recorded as committed");
        await ((Func<Task>)(() => outsider.Writer.MarkCommittedAsync(staged.Commit(harness.Clock.GetUtcNow()), CancellationToken.None)))
            .Should().ThrowAsync<ArgumentException>("the set names another organization than the store's");

        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeEquivalentTo(staged);
    }

    [Fact]
    public async Task MarkCommittedAsync_WithAnAlreadyCancelledToken_Throws_AndLeavesTheSetStaged()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await ((Func<Task>)(() => harness.Writer.MarkCommittedAsync(staged.Commit(harness.Clock.GetUtcNow()), cancelled.Token)))
            .Should().ThrowAsync<OperationCanceledException>();

        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeEquivalentTo(staged);
    }
}
