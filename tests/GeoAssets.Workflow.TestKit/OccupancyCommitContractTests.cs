using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Behavioural contract of the approval that commits staged occupancy (XD01-318) —
/// <see cref="IOccupancyCommitService"/> — over whatever stores a runner puts under it. The same
/// facts run on the in-memory TestKit stores, on EF Core over SQLite and on EF Core over
/// PostgreSQL, so "approve and commit" means one thing on all three. All-or-nothing is asserted
/// by the database runners only: the in-memory unit of work has nothing to roll back.
///
/// Subclass it, implement <see cref="CreateHarnessAsync"/>, and declare at least one
/// <c>[Fact]</c> of your own on the subclass (xUnit only discovers <c>public</c> classes).
/// </summary>
public abstract class OccupancyCommitContractTests
{
    protected const string PoleType = "pole";

    protected static readonly Guid OccupantOne = new("0c0c0c0c-0000-4000-8000-000000000001");
    protected static readonly Guid OccupantTwo = new("0c0c0c0c-0000-4000-8000-000000000002");

    private static readonly DateTime CreatedAt = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A fresh approval over empty stores, all scoped to one pole-owner organization.</summary>
    protected abstract Task<OccupancyCommitContractHarness> CreateHarnessAsync();

    // ── Arrange helpers ───────────────────────────────────────────────────────

    /// <summary>A supervisor of the pole owner's organization: may approve.</summary>
    protected static WorkflowPrincipal Approver(OccupancyCommitContractHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return new WorkflowPrincipal("approver-1", harness.OwnerOrganizationId.ToString(), ["Supervisor"], [], []);
    }

    /// <summary>The id the suite's targets have: the one selection gives a pole on an order.</summary>
    protected static string TargetIdOf(string orderId, string pole) => OrderTargetSelectionService.TargetId(orderId, pole);

    /// <summary>A pole-work order of the owner in <paramref name="status"/>, holding one target per pole.</summary>
    protected static async Task SeedOrderAsync(
        OccupancyCommitContractHarness harness, string orderId, string status, params string[] poles)
    {
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(poles);
        if (harness.OrderTypes.Find(SharedUseOrderTypes.PoleWorkId) is null)
            SharedUseOrderTypes.RegisterAll(harness.OrderTypes);

        await harness.Orders.AddAsync(new ServiceOrder
        {
            Id = orderId,
            Title = $"Pole work {orderId}",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            Status = status,
            CreatedBy = "planner-1",
            OrganizationId = harness.OwnerOrganizationId,
            CreatedAt = CreatedAt,
        });
        await harness.TargetWriter.AddRangeAsync(
            orderId,
            [.. poles.Select(pole => new ServiceOrderTargetBuilder()
                .WithId(TargetIdOf(orderId, pole))
                .WithOrderId(orderId)
                .WithFeatureId(pole)
                .WithAssetTypeId(PoleType)
                .WithOrganizationId(harness.OwnerOrganizationId)
                .Build())],
            CancellationToken.None);
    }

    /// <summary>Puts or replaces a pole's live occupancy, at whatever version it is.</summary>
    protected static async Task<PoleOccupancy> OccupyAsync(OccupancyCommitContractHarness harness, string pole, params PoleOccupancyEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(harness);
        var live = await harness.OccupancyReader.GetForPoleAsync(pole, CancellationToken.None);
        return await harness.OccupancyWriter.ReplaceEntriesAsync(
            pole, live?.Version ?? PoleOccupancy.UnsavedVersion, entries, CancellationToken.None);
    }

    /// <summary>A contracted, identified entry of <paramref name="occupant"/> on a regulated slot of a pole of the owner.</summary>
    protected static PoleOccupancyEntry Entry(OccupancyCommitContractHarness harness, string pole, int slot, Guid occupant)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(harness.OwnerOrganizationId).OnSlot(slot).OccupiedBy(occupant).Build();
    }

    /// <summary>Stages <paramref name="changes"/> on the suite's target of <paramref name="pole"/>, against the pole as it is now.</summary>
    protected static async Task<OccupancyChangeSet> StageAsync(
        OccupancyCommitContractHarness harness, string orderId, string pole, params OccupancyChange[] changes)
    {
        ArgumentNullException.ThrowIfNull(harness);
        var baseline = await harness.OccupancyReader.GetForPoleAsync(pole, CancellationToken.None);
        var set = OccupancyChangeSet.Stage(
            orderId, TargetIdOf(orderId, pole), pole, harness.OwnerOrganizationId, changes, baseline, "crew-1", harness.Clock.GetUtcNow());
        return await harness.ChangeSetWriter.StageAsync(set, CancellationToken.None);
    }

    /// <summary>An Add, beyond the regulated slots, of an attachment nobody is identified for.</summary>
    protected static OccupancyChange UnidentifiedBeyondTheSlots() =>
        OccupancyChangeBuilder.AddBeyondRegulatedSlots() with
        {
            Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
            OccupantOrganizationId = null,
            OperatorCnpj = null,
        };

    protected static Task<OccupancyCommitResult> ApproveAsync(OccupancyCommitContractHarness harness, string orderId)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return harness.Service.ApproveAndCommitAsync(orderId, Approver(harness), CancellationToken.None);
    }

    protected static async Task<IReadOnlyList<OccupancyChangeSet>> SetsAsync(
        OccupancyCommitContractHarness harness, string orderId, OccupancyChangeSetStatus? status = null)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return (await harness.ChangeSetReader.GetPageAsync(
            new OccupancyChangeSetQuery(orderId, pageSize: OccupancyChangeSetQuery.MaxPageSize, status: status), CancellationToken.None)).Items;
    }

    // ── The approval ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAndCommitAsync_CommitsEveryStagedSetIntoItsPole_IncludingEntriesBeyondTheSlots_AndApprovesTheOrder()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        var first = await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));
        var second = await StageAsync(
            harness, "ord-1", "pole-2",
            [.. Enumerable.Range(1, 6).Select(slot => OccupancyChangeBuilder.Add(slot)), UnidentifiedBeyondTheSlots()]);
        harness.Clock.Advance(TimeSpan.FromHours(1));
        var approvedAt = harness.Clock.GetUtcNow();

        var result = await ApproveAsync(harness, "ord-1");

        result.Should().BeEquivalentTo(new OccupancyCommitResult
        {
            OrderId = "ord-1",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            PreviousStatus = ServiceOrderStatus.Completed,
            Status = SharedUseOrderTypes.ApprovedStateKey,
            ChangeSetsCommitted = 2,
            ChangeSetsDiscarded = 0,
            ChangesCommitted = 8,
            ChangesByKind = new Dictionary<OccupancyChangeKind, int> { [OccupancyChangeKind.Add] = 8 },
            PolesUpdated = 2,
            AssetMutationBatchId = OccupancyCommitService.AssetMutationBatchId("ord-1"),
        });

        var pole1 = (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!;
        pole1.Version.Should().Be(2);
        pole1.Entries.Select(e => e.SlotIndex).Should().Equal(1, 2);
        var pole2 = (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None))!;
        pole2.Version.Should().Be(PoleOccupancy.FirstVersion);
        pole2.Entries.Should().HaveCount(7, "the attachment beyond the six slots is committed, not dropped");
        pole2.OverCapacityEntries.Should().ContainSingle().Which.Id.Should().Be(second.Changes[^1].EntryId!.Value);
        pole2.IsOverCapacity.Should().BeTrue();
        pole2.HasUnidentifiedOccupant.Should().BeTrue();
        pole2.Class.Should().Be(OccupancyClass.Unidentified, "unidentified takes precedence over over capacity");

        var sets = await SetsAsync(harness, "ord-1");
        sets.Should().HaveCount(2).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Committed && set.ClosedAt == approvedAt,
            "closing the order did not discard what the approval committed");
        sets.Select(set => set.Id).Should().BeEquivalentTo([first.Id, second.Id]);

        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        order.CompletedAt.Should().Be(approvedAt.UtcDateTime);
        order.ActionLog.Should().ContainSingle().Which.Should().Match<OrderActionLog>(entry =>
            entry.Action == OrderActionType.Approve && entry.PerformedBy == "approver-1"
            && entry.ResultingStatus == SharedUseOrderTypes.ApprovedStateKey && entry.PerformedAt == approvedAt.UtcDateTime);
        (await harness.TargetReader.FindOpenUsagesAsync(["pole-1", "pole-2"], "ord-other", CancellationToken.None))
            .Should().BeEmpty("an approved order releases its poles");
    }

    [Fact]
    public async Task ApproveAndCommitAsync_StagesOneAssetMutationBatch_WithEachWrittenPolesOccupancyAttributes()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-a", "pole-b", "pole-c");
        await OccupyAsync(harness, "pole-a", Entry(harness, "pole-a", 1, OccupantOne));
        await StageAsync(harness, "ord-1", "pole-a", OccupancyChangeBuilder.RemoveSlot(1));
        await StageAsync(harness, "ord-1", "pole-b", [.. Enumerable.Range(1, 6).Select(slot => OccupancyChangeBuilder.Add(slot))]);
        await StageAsync(
            harness, "ord-1", "pole-c",
            [.. Enumerable.Range(1, 6).Select(slot => OccupancyChangeBuilder.Add(slot)), OccupancyChangeBuilder.AddBeyondRegulatedSlots()]);

        await ApproveAsync(harness, "ord-1");

        var staged = (await harness.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle().Subject;
        staged.MessageType.Should().Be(IntegrationEventTypes.AssetMutationBatchRequestedV1);
        staged.PartitionKey.Should().Be("ord-1");
        staged.CorrelationId.Should().Be("ord-1");
        staged.CausationId.Should().Be(OccupancyCommitService.CausationId("ord-1"));
        var batch = staged.Batch;
        batch.BatchId.Should().Be(OccupancyCommitService.AssetMutationBatchId("ord-1"));
        batch.CatalogInstanceId.Should().Be(harness.CatalogInstanceId);
        batch.OrganizationId.Should().Be(harness.OwnerOrganizationId);
        batch.RequestedByService.Should().Be(OccupancyCommitService.RequestedByService);
        batch.PerformedByUserId.Should().Be("approver-1");
        batch.SourceOrderId.Should().Be("ord-1");
        batch.SourceOrderTypeId.Should().Be(SharedUseOrderTypes.PoleWorkId);
        batch.CorrelationId.Should().Be("ord-1");
        batch.RequestedAtUtc.Should().Be(harness.Clock.GetUtcNow());
        batch.Mutations.Select(m => m.FeatureId).Should().Equal("pole-a", "pole-b", "pole-c");
        batch.Mutations.Should().OnlyContain(m => m.MutationKind == AssetMutationKind.UpdateAttributes && m.AssetTypeId == PoleType);
        batch.Mutations.Select(m => m.CustomAttributes).Should().BeEquivalentTo(
        [
            Attributes(OccupancyClass.None, 0, overCapacity: false, unidentified: false),
            Attributes(OccupancyClass.High, 6, overCapacity: false, unidentified: false),
            Attributes(OccupancyClass.OverCapacity, 7, overCapacity: true, unidentified: false),
        ]);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_WithNothingStaged_StillApprovesTheOrder_AndStagesNoBatch()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");

        var result = await ApproveAsync(harness, "ord-1");

        result.ChangeSetsCommitted.Should().Be(0);
        result.ChangesCommitted.Should().Be(0);
        result.ChangesByKind.Should().BeEmpty();
        result.PolesUpdated.Should().Be(0);
        result.AssetMutationBatchId.Should().BeNull();
        (await harness.Orders.GetByIdAsync("ord-1"))!.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().BeEmpty();
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData(OrderTargetStatus.Skipped)]
    [InlineData(OrderTargetStatus.NotExecutable)]
    public async Task ApproveAndCommitAsync_ASetOnATargetThatWasNotWorked_IsDiscarded_AndItsPoleIsLeftAsItWas(string targetStatus)
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantOne));
        var worked = await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        var notWorked = await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.RemoveSlot(1));
        var target = (await harness.TargetReader.GetByIdAsync("ord-1", TargetIdOf("ord-1", "pole-2"), CancellationToken.None))!;
        if (string.Equals(targetStatus, OrderTargetStatus.NotExecutable, StringComparison.Ordinal))
        {
            await harness.TargetWriter.UpdateAsync(target.WithStatus(OrderTargetStatus.InProgress), CancellationToken.None);
            target = (await harness.TargetReader.GetByIdAsync("ord-1", target.Id, CancellationToken.None))!;
        }

        await harness.TargetWriter.UpdateAsync(target.WithStatus(targetStatus), CancellationToken.None);

        var result = await ApproveAsync(harness, "ord-1");

        result.ChangeSetsCommitted.Should().Be(1);
        result.ChangeSetsDiscarded.Should().Be(1);
        result.PolesUpdated.Should().Be(1);
        var sets = await SetsAsync(harness, "ord-1");
        sets.Single(set => set.Id == worked.Id).Status.Should().Be(OccupancyChangeSetStatus.Committed);
        sets.Single(set => set.Id == notWorked.Id).Status.Should().Be(OccupancyChangeSetStatus.Discarded);
        var pole2 = (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None))!;
        pole2.Version.Should().Be(PoleOccupancy.FirstVersion);
        pole2.Entries.Should().ContainSingle();
        (await harness.StagedBatchesAsync(CancellationToken.None)).Single().Batch.Mutations.Select(m => m.FeatureId).Should().Equal("pole-1");
    }

    // ── Refusals that leave everything as it was ──────────────────────────────

    [Fact]
    public async Task ApproveAndCommitAsync_APoleThatMovedSinceStaging_IsRefused_NamingEveryStalePole_AndNothingIsCommitted()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2", "pole-3");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await OccupyAsync(harness, "pole-3", Entry(harness, "pole-3", 1, OccupantOne));
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));
        await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.Add(1));
        await StageAsync(harness, "ord-1", "pole-3", OccupancyChangeBuilder.Add(2));
        // Another order's commit, or a data correction, wins the poles first.
        await OccupyAsync(harness, "pole-3", Entry(harness, "pole-3", 1, OccupantTwo));
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantTwo));

        var act = () => ApproveAsync(harness, "ord-1");

        (await act.Should().ThrowAsync<OccupancyConflictException>()).Which.PoleFeatureIds.Should().Equal("pole-1", "pole-3");
        await AssertNothingCommittedAsync(harness, "ord-1", stagedSets: 3);
        (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None)).Should().BeNull("the pole that had not moved was not written either");
    }

    [Fact]
    public async Task ApproveAndCommitAsync_Repeated_HitsTheTransitionGuard_AndNothingIsAppliedTwice()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");

        var again = () => ApproveAsync(harness, "ord-1");

        var refused = (await again.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which;
        refused.From.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(PoleOccupancy.FirstVersion);
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle();
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Should().ContainSingle();
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public async Task ApproveAndCommitAsync_FromAStatusWithNoApproval_ThrowsInvalidServiceOrderTransitionException_AndCommitsNothing(string status)
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", status, "pole-1");

        var act = () => ApproveAsync(harness, "ord-1");

        var refused = (await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which;
        refused.From.Should().Be(status);
        refused.To.Should().Be(status);
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_ByAMemberTheRulesRefuse_OrByAnotherOrganization_IsDenied_AndCommitsNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        var memberWithoutRole = new WorkflowPrincipal("crew-9", harness.OwnerOrganizationId.ToString(), [], [], []);
        var otherAdministrator = new WorkflowPrincipal("admin-x", Guid.NewGuid().ToString(), ["Administrator"], [], []);

        var byMember = () => harness.Service.ApproveAndCommitAsync("ord-1", memberWithoutRole, CancellationToken.None);
        var byOutsider = () => harness.Service.ApproveAndCommitAsync("ord-1", otherAdministrator, CancellationToken.None);

        (await byMember.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.IsOutsideOrganization.Should().BeFalse();
        (await byOutsider.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.IsOutsideOrganization.Should().BeTrue();
        await AssertNothingCommittedAsync(harness, "ord-1", stagedSets: 1);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_UnknownOrder_ThrowsKeyNotFoundException()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => ApproveAsync(harness, "ord-missing");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_WithAnAlreadyCancelledToken_Throws_AndCommitsNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => harness.Service.ApproveAndCommitAsync("ord-1", Approver(harness), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertNothingCommittedAsync(harness, "ord-1", stagedSets: 1);
    }

    // ── Assertion helpers ─────────────────────────────────────────────────────

    /// <summary>The order is still completed, unlogged, its sets all staged, and no batch was staged.</summary>
    protected static async Task AssertNothingCommittedAsync(OccupancyCommitContractHarness harness, string orderId, int stagedSets)
    {
        ArgumentNullException.ThrowIfNull(harness);

        var order = (await harness.Orders.GetByIdAsync(orderId))!;
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        order.ActionLog.Should().BeEmpty();
        (await SetsAsync(harness, orderId)).Should().HaveCount(stagedSets).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Staged);
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().BeEmpty();
    }

    private static Dictionary<string, string> Attributes(OccupancyClass occupancyClass, int occupied, bool overCapacity, bool unidentified) => new(StringComparer.Ordinal)
    {
        [OccupancyAssetAttributes.ClassKey] = occupancyClass.ToString(),
        [OccupancyAssetAttributes.OccupiedCountKey] = occupied.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [OccupancyAssetAttributes.OverCapacityKey] = overCapacity ? "true" : "false",
        [OccupancyAssetAttributes.UnidentifiedKey] = unidentified ? "true" : "false",
    };
}
