using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Liskov Substitution Principle behavioural contract for every
/// <see cref="IOccupancyChangeSetReader"/> + <see cref="IOccupancyChangeSetWriter"/> adapter
/// (XD01-315). Each fact pins one sentence of the member documentation on those two interfaces,
/// so an adapter that passes here can replace any other.
///
/// Subclass it, implement <see cref="CreateHarnessAsync"/>, and declare at least one
/// <c>[Fact]</c> of your own on the subclass — xUnit only discovers <c>public</c> classes, and a
/// class whose tests are all inherited is the shape analyzers push back to <c>internal</c>.
///
/// The facts are split over two files: staging and discarding (here), and reading, the
/// target/order life cycle and tenancy (<c>.Reads.cs</c>).
/// </summary>
public abstract partial class OccupancyChangeSetStoreContractTests
{
    /// <summary>An organization that is never the harness's own.</summary>
    protected static readonly Guid OtherOrganizationId = new("0b0b0b0b-0000-4000-8000-000000000002");

    private static readonly DateTime CreatedAt = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A fresh, empty store scoped to one organization, beside empty order and target stores.</summary>
    protected abstract Task<OccupancyChangeSetStoreContractHarness> CreateHarnessAsync();

    // ── Arrange helpers ───────────────────────────────────────────────────────

    /// <summary>The id the suite gives the target of <paramref name="featureId"/> on <paramref name="orderId"/>.</summary>
    protected static string TargetId(string orderId, string featureId) => $"{orderId}:{featureId}";

    /// <summary>Adds a pole target to an order of the harness's organization, creating the order on first use.</summary>
    protected static async Task<ServiceOrderTarget> SeedTargetAsync(
        OccupancyChangeSetStoreContractHarness harness, string orderId, string featureId)
    {
        ArgumentNullException.ThrowIfNull(harness);

        if (await harness.Orders.GetByIdAsync(orderId) is null)
        {
            await harness.Orders.AddAsync(new ServiceOrder
            {
                Id = orderId,
                Title = orderId,
                OrderTypeId = "inspection",
                Status = ServiceOrderStatus.Draft,
                CreatedBy = "operator-1",
                OrganizationId = harness.OrganizationId,
                CreatedAt = CreatedAt,
            });
        }

        var target = new ServiceOrderTargetBuilder()
            .WithId(TargetId(orderId, featureId))
            .WithOrderId(orderId)
            .WithFeatureId(featureId)
            .WithAlias(null)
            .WithOrganizationId(harness.OrganizationId)
            .Build();
        await harness.Targets.AddRangeAsync(orderId, [target], CancellationToken.None);
        return target;
    }

    /// <summary>Moves an order along <paramref name="statuses"/>, one legal transition at a time.</summary>
    protected static async Task MoveOrderAsync(OccupancyChangeSetStoreContractHarness harness, string orderId, params string[] statuses)
    {
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(statuses);

        foreach (var status in statuses)
        {
            var order = (ServiceOrder)(await harness.Orders.GetByIdAsync(orderId))!;
            order.Status = status;
            await harness.Orders.UpdateAsync(order);
        }
    }

    /// <summary>A staged set for the suite's target of <paramref name="featureId"/> on <paramref name="orderId"/>.</summary>
    protected static OccupancyChangeSet NewSet(
        OccupancyChangeSetStoreContractHarness harness, string orderId, string featureId,
        IReadOnlyCollection<OccupancyChange>? changes = null, PoleOccupancy? baseline = null, string stagedBy = "crew-1")
    {
        ArgumentNullException.ThrowIfNull(harness);

        return OccupancyChangeSet.Stage(
            orderId, TargetId(orderId, featureId), featureId, harness.OrganizationId,
            changes ?? [OccupancyChangeBuilder.Add(slotIndex: 2)], baseline, stagedBy, harness.Clock.GetUtcNow());
    }

    private static Task<OccupancyChangeSet> StageAsync(
        OccupancyChangeSetStoreContractHarness harness, string orderId, string featureId, params OccupancyChange[] changes) =>
        harness.Writer.StageAsync(
            NewSet(harness, orderId, featureId, changes.Length == 0 ? null : changes), CancellationToken.None);

    private static async Task<IReadOnlyList<OccupancyChangeSet>> AllSetsAsync(
        OccupancyChangeSetStoreContractHarness harness, string orderId, OccupancyChangeSetStatus? status = null) =>
        (await harness.Reader.GetPageAsync(
            new OccupancyChangeSetQuery(orderId, pageSize: OccupancyChangeSetQuery.MaxPageSize, status: status), CancellationToken.None)).Items;

    // ── StageAsync: what is stored ────────────────────────────────────────────

    [Fact]
    public async Task StageAsync_NewSet_IsReturnedAsStored_AndReadBackWithEveryField()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var existing = new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(harness.OrganizationId).BeyondRegulatedSlots().Build();
        var baseline = PoleOccupancy.Create("pole-1", harness.OrganizationId, [existing], version: 7, harness.Clock.GetUtcNow());
        OccupancyChange[] changes =
        [
            OccupancyChangeBuilder.Add(3, status: PoleAttachmentOccupantStatus.ActiveReservation) with
            {
                OccupantOrganizationId = null, OperatorCnpj = null, AnatelLicenseNumber = "SCM-0042",
                CableDiameterMm = 12.345m, CableLoadDaN = 150.125m, AttachedCableFeatureId = "cable-7",
            },
            OccupancyChangeBuilder.AddBeyondRegulatedSlots(),
            OccupancyChangeBuilder.ModifyEntry(existing.Id, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
            OccupancyChangeBuilder.RemoveSlot(5),
            OccupancyChangeBuilder.MarkSlotUnidentified(6),
        ];
        var set = NewSet(harness, "ord-1", "pole-1", changes, baseline, stagedBy: "crew-7");

        var stored = await harness.Writer.StageAsync(set, CancellationToken.None);

        stored.Should().BeEquivalentTo(set);
        stored.BaselineVersion.Should().Be(7);
        stored.Changes.Should().Equal(changes);
        var read = await harness.Reader.GetStagedAsync("ord-1", TargetId("ord-1", "pole-1"), CancellationToken.None);
        read.Should().BeEquivalentTo(stored);
        read!.Changes.Should().Equal(changes, "the changes come back in the order they were staged");
    }

    [Fact]
    public async Task StageAsync_AgainForTheSameTarget_ReplacesTheStagedSet_AndTheReplacedOneIsGone()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var first = await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));

        var second = await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(4), OccupancyChangeBuilder.AddBeyondRegulatedSlots());

        second.Id.Should().NotBe(first.Id);
        (await harness.Reader.GetStagedAsync("ord-1", second.TargetId, CancellationToken.None)).Should().BeEquivalentTo(second);
        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task StageAsync_TheSameSetTwice_IsSafe_AndLeavesOneStagedSet()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var set = NewSet(harness, "ord-1", "pole-1");

        await harness.Writer.StageAsync(set, CancellationToken.None);
        var again = await harness.Writer.StageAsync(set, CancellationToken.None);

        again.Should().BeEquivalentTo(set);
        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Should().BeEquivalentTo(set);
    }

    [Fact]
    public async Task StageAsync_ASetWithAsManyChangesAsASetHolds_RoundTrips()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var changes = Enumerable.Range(0, OccupancyChangeSet.MaxChanges).Select(_ => OccupancyChangeBuilder.AddBeyondRegulatedSlots()).ToArray();

        var stored = await StageAsync(harness, "ord-1", "pole-1", changes);

        stored.Changes.Should().Equal(changes);
        (await harness.Reader.GetStagedAsync("ord-1", stored.TargetId, CancellationToken.None))!.Changes.Should().Equal(changes);
    }

    [Fact]
    public async Task StageAsync_TwoTargetsOfOneOrder_AndTheSamePoleOnTwoOrders_EachKeepTheirOwnStagedSet()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-1", "pole-2");
        await SeedTargetAsync(harness, "ord-2", "pole-1");

        var a = await StageAsync(harness, "ord-1", "pole-1");
        var b = await StageAsync(harness, "ord-1", "pole-2");
        var c = await StageAsync(harness, "ord-2", "pole-1");

        (await harness.Reader.GetStagedAsync("ord-1", a.TargetId, CancellationToken.None))!.Id.Should().Be(a.Id);
        (await harness.Reader.GetStagedAsync("ord-1", b.TargetId, CancellationToken.None))!.Id.Should().Be(b.Id);
        (await harness.Reader.GetStagedAsync("ord-2", c.TargetId, CancellationToken.None))!.Id.Should().Be(c.Id);
        (await AllSetsAsync(harness, "ord-1")).Select(set => set.Id).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    // ── StageAsync: what is refused ───────────────────────────────────────────

    [Fact]
    public async Task StageAsync_ASetThatIsNotStaged_ThrowsArgumentException_AndStoresNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var discarded = NewSet(harness, "ord-1", "pole-1").Discard(harness.Clock.GetUtcNow());

        var act = () => harness.Writer.StageAsync(discarded, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        (await AllSetsAsync(harness, "ord-1")).Should().BeEmpty();
    }

    [Fact]
    public async Task StageAsync_ASetOwnedByAnotherOrganization_ThrowsArgumentException_AndStoresNothing()
    {
        await using var harness = await CreateHarnessAsync();
        var target = await SeedTargetAsync(harness, "ord-1", "pole-1");
        var foreign = OccupancyChangeSet.Stage(
            "ord-1", target.Id, "pole-1", OtherOrganizationId, [OccupancyChangeBuilder.Add(1)], null, "crew-1", harness.Clock.GetUtcNow());

        var act = () => harness.Writer.StageAsync(foreign, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        (await AllSetsAsync(harness, "ord-1")).Should().BeEmpty();
        var asTheOther = await harness.AsOrganization(OtherOrganizationId).Reader
            .GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None);
        asTheOther.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task StageAsync_UnknownOrder_UnknownTarget_TargetOfAnotherOrder_OrAnotherPole_ThrowKeyNotFoundException()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await SeedTargetAsync(harness, "ord-2", "pole-2");
        OccupancyChangeSet Set(string orderId, string targetId, string pole) => OccupancyChangeSet.Stage(
            orderId, targetId, pole, harness.OrganizationId, [OccupancyChangeBuilder.Add(1)], null, "crew-1", harness.Clock.GetUtcNow());

        OccupancyChangeSet[] unreachable =
        [
            Set("ord-missing", TargetId("ord-1", "pole-1"), "pole-1"),
            Set("ord-1", "tgt-missing", "pole-1"),
            Set("ord-1", TargetId("ord-2", "pole-2"), "pole-2"),
            Set("ord-1", TargetId("ord-1", "pole-1"), "pole-other"),
        ];

        foreach (var set in unreachable)
        {
            var act = () => harness.Writer.StageAsync(set, CancellationToken.None);
            await act.Should().ThrowAsync<KeyNotFoundException>($"order '{set.OrderId}' holds no target '{set.TargetId}' for pole '{set.PoleFeatureId}'");
        }

        (await AllSetsAsync(harness, "ord-1")).Should().BeEmpty();
        (await AllSetsAsync(harness, "ord-2")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.Completed)]
    public async Task StageAsync_ForATargetReleasedBecauseItsOrderClosed_ThrowsOccupancyTargetReleased_AndStoresNothing(string closedAs)
    {
        ArgumentNullException.ThrowIfNull(closedAs);
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        await MoveOrderAsync(harness, "ord-1", string.Equals(closedAs, ServiceOrderStatus.Completed, StringComparison.Ordinal)
            ? [ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed]
            : [ServiceOrderStatus.Cancelled]);

        var act = () => harness.Writer.StageAsync(NewSet(harness, "ord-1", "pole-1"), CancellationToken.None);

        (await act.Should().ThrowAsync<OccupancyTargetReleasedException>()).Which.Should().BeEquivalentTo(
            new { OrderId = "ord-1", TargetId = TargetId("ord-1", "pole-1") });
        (await AllSetsAsync(harness, "ord-1")).Should().BeEmpty();
    }

    [Fact]
    public async Task StageAsync_TheIdOfASetAlreadyDiscarded_ThrowsArgumentException_AndTheDiscardedSetStands()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var set = NewSet(harness, "ord-1", "pole-1");
        await harness.Writer.StageAsync(set, CancellationToken.None);
        await harness.Writer.DiscardAsync("ord-1", set.TargetId, CancellationToken.None);

        var act = () => harness.Writer.StageAsync(set, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Discarded);
    }

    [Fact]
    public async Task StageAsync_NullSet_ThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Writer.StageAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StageAsync_AlreadyCancelledToken_Throws_AndWhatWasStagedBeforeIsStillStaged()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var before = await StageAsync(harness, "ord-1", "pole-1");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => harness.Writer.StageAsync(NewSet(harness, "ord-1", "pole-1"), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await harness.Reader.GetStagedAsync("ord-1", before.TargetId, CancellationToken.None))!.Id.Should().Be(before.Id);
    }

    // ── DiscardAsync / DiscardAllAsync ────────────────────────────────────────

    [Fact]
    public async Task DiscardAsync_AStagedSet_ReturnsTrue_AndKeepsItOnFileAsDiscardedClosedAtTheClock()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        harness.Clock.Advance(TimeSpan.FromMinutes(30));

        var discarded = await harness.Writer.DiscardAsync("ord-1", staged.TargetId, CancellationToken.None);

        discarded.Should().BeTrue();
        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeNull();
        var onFile = (await AllSetsAsync(harness, "ord-1")).Should().ContainSingle().Subject;
        onFile.Should().BeEquivalentTo(staged, options => options.Excluding(set => set.Status).Excluding(set => set.ClosedAt));
        onFile.Status.Should().Be(OccupancyChangeSetStatus.Discarded);
        onFile.ClosedAt.Should().Be(harness.Clock.GetUtcNow());
    }

    [Fact]
    public async Task DiscardAsync_NothingStaged_ReturnsFalse_SoRepeatingItIsHarmless()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        await harness.Writer.DiscardAsync("ord-1", staged.TargetId, CancellationToken.None);
        var closedAt = (await AllSetsAsync(harness, "ord-1")).Single().ClosedAt;
        harness.Clock.Advance(TimeSpan.FromHours(1));

        (await harness.Writer.DiscardAsync("ord-1", staged.TargetId, CancellationToken.None)).Should().BeFalse();
        (await harness.Writer.DiscardAsync("ord-1", "tgt-missing", CancellationToken.None)).Should().BeFalse();
        (await harness.Writer.DiscardAsync("ord-missing", staged.TargetId, CancellationToken.None)).Should().BeFalse();
        (await AllSetsAsync(harness, "ord-1")).Single().ClosedAt.Should().Be(closedAt, "a discarded set is not discarded again");
    }

    [Fact]
    public async Task DiscardAsync_ThenStagingAgain_StagesAFreshSet_AndKeepsTheDiscardedOne()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var first = await StageAsync(harness, "ord-1", "pole-1");
        await harness.Writer.DiscardAsync("ord-1", first.TargetId, CancellationToken.None);

        var second = await StageAsync(harness, "ord-1", "pole-1");

        (await harness.Reader.GetStagedAsync("ord-1", first.TargetId, CancellationToken.None))!.Id.Should().Be(second.Id);
        (await AllSetsAsync(harness, "ord-1", OccupancyChangeSetStatus.Discarded)).Should().ContainSingle().Which.Id.Should().Be(first.Id);
        (await AllSetsAsync(harness, "ord-1")).Should().HaveCount(2);
    }

    [Fact]
    public async Task DiscardAllAsync_DiscardsEveryStagedSetOfThatOrderOnly_AndReturnsHowMany()
    {
        await using var harness = await CreateHarnessAsync();
        foreach (var pole in new[] { "pole-1", "pole-2", "pole-3" })
            await SeedTargetAsync(harness, "ord-1", pole);
        await SeedTargetAsync(harness, "ord-2", "pole-1");
        await StageAsync(harness, "ord-1", "pole-1");
        await StageAsync(harness, "ord-1", "pole-2");
        var alreadyDiscarded = await StageAsync(harness, "ord-1", "pole-3");
        await harness.Writer.DiscardAsync("ord-1", alreadyDiscarded.TargetId, CancellationToken.None);
        var other = await StageAsync(harness, "ord-2", "pole-1");

        var discarded = await harness.Writer.DiscardAllAsync("ord-1", CancellationToken.None);

        discarded.Should().Be(2, "the set discarded earlier is not counted again");
        (await AllSetsAsync(harness, "ord-1")).Should().HaveCount(3).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Discarded);
        (await harness.Reader.GetStagedAsync("ord-2", other.TargetId, CancellationToken.None))!.Id.Should().Be(other.Id);
        (await harness.Writer.DiscardAllAsync("ord-1", CancellationToken.None)).Should().Be(0);
        (await harness.Writer.DiscardAllAsync("ord-missing", CancellationToken.None)).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Discarding_WithABlankId_ThrowsArgumentException(string? blank)
    {
        await using var harness = await CreateHarnessAsync();

        await ((Func<Task>)(() => harness.Writer.DiscardAsync(blank!, "tgt-1", CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => harness.Writer.DiscardAsync("ord-1", blank!, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => harness.Writer.DiscardAllAsync(blank!, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Discarding_WithAnAlreadyCancelledToken_Throws_AndDiscardsNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var staged = await StageAsync(harness, "ord-1", "pole-1");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await ((Func<Task>)(() => harness.Writer.DiscardAsync("ord-1", staged.TargetId, cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => harness.Writer.DiscardAllAsync("ord-1", cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
        (await harness.Reader.GetStagedAsync("ord-1", staged.TargetId, CancellationToken.None))!.Id.Should().Be(staged.Id);
    }
}
