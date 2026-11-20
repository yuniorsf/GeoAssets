using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
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
    public async Task ApproveAndCommitAsync_WritesEveryStagedSetIntoItsPole_IncludingEntriesBeyondTheSlots_AsPending_AndTheOrderAwaitsTheCatalog()
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
            Status = ServiceOrderStatus.Completed,
            AwaitingApply = true,
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
        sets.Should().HaveCount(2).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Pending && set.ClosedAt == approvedAt,
            "the sets are applied to the poles but wait for the catalog's answer");
        sets.Select(set => set.Id).Should().BeEquivalentTo([first.Id, second.Id]);

        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Status.Should().Be(ServiceOrderStatus.Completed, "the approval is final only when the catalog applied it");
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.AwaitingApply);
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.BatchIdKey, OccupancyCommitService.AssetMutationBatchId("ord-1").ToString("D"));
        order.ActionLog.Should().ContainSingle().Which.Should().Match<OrderActionLog>(entry =>
            entry.Action == OrderActionType.Approve && entry.PerformedBy == "approver-1"
            && entry.ResultingStatus == null && entry.PerformedAt == approvedAt.UtcDateTime);

        var saga = (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!;
        saga.State.Should().Be(OccupancyApplySagaState.Pending);
        saga.Attempt.Should().Be(1);
        saga.BatchId.Should().Be(OccupancyCommitService.AssetMutationBatchId("ord-1"));
        saga.PreviousStatus.Should().Be(ServiceOrderStatus.Completed);
        saga.ApprovedStatus.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        saga.RequestedBy.Should().Be("approver-1");
        saga.RequestedAt.Should().Be(approvedAt);
        var baselines = await harness.SagaReader.GetPolesAsync(saga.BatchId, CancellationToken.None);
        baselines.Select(p => (p.PoleFeatureId, p.BaselineVersion, p.AppliedVersion, p.BaselineEntries.Count))
            .Should().Equal(("pole-1", 1, 2, 1), ("pole-2", 0, 1, 0));
    }

    [Fact]
    public async Task ApplyReply_WhenTheCatalogAppliesTheBatch_CommitsTheSets_ApprovesTheOrder_AndLogsIt()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        var first = await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));
        var second = await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        var appliedAt = harness.Clock.GetUtcNow();

        var result = await ReplyAppliedAsync(harness);

        result.Outcome.Should().Be(OccupancyApplySagaOutcome.Committed);
        var sets = await SetsAsync(harness, "ord-1");
        sets.Should().HaveCount(2).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Committed && set.ClosedAt == appliedAt);
        sets.Select(set => set.Id).Should().BeEquivalentTo([first.Id, second.Id]);
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        order.CompletedAt.Should().Be(appliedAt.UtcDateTime);
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Applied);
        order.ActionLog.Select(entry => entry.Action).Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Applied);
        order.ActionLog[1].Should().Match<OrderActionLog>(entry =>
            entry.ResultingStatus == SharedUseOrderTypes.ApprovedStateKey && entry.PerformedAt == appliedAt.UtcDateTime);
        var saga = (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!;
        saga.State.Should().Be(OccupancyApplySagaState.Committed);
        saga.CompletedAt.Should().Be(appliedAt);
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(2, "the poles keep what the approval wrote");
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
        batch.SourceVerticalId.Should().Be(OccupancyApplySagaService.VerticalId, "the kernel routes its reply to the vertical by this id");
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
        sets.Single(set => set.Id == worked.Id).Status.Should().Be(OccupancyChangeSetStatus.Pending);
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
    public async Task ApproveAndCommitAsync_RepeatedWhileTheCatalogIsStillApplying_IsRefusedAsApplyPending_AndNothingIsAppliedTwice()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");

        var again = () => ApproveAsync(harness, "ord-1");

        var refused = (await again.Should().ThrowAsync<OccupancyApplyPendingException>()).Which;
        refused.OrderId.Should().Be("ord-1");
        refused.PoleFeatureIds.Should().BeEmpty("the order itself awaits the catalog");
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(PoleOccupancy.FirstVersion);
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle();
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_RepeatedAfterTheCatalogApplied_HitsTheTransitionGuard_AndNothingIsAppliedTwice()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        await ReplyAppliedAsync(harness);

        var again = () => ApproveAsync(harness, "ord-1");

        var refused = (await again.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which;
        refused.From.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(PoleOccupancy.FirstVersion);
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle();
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Select(entry => entry.Action)
            .Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Applied);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_AnotherOrdersPoleHeldByAPendingApproval_IsRefusedNamingThePole_AndNothingIsCommitted()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await SeedOrderAsync(harness, "ord-2", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        // Staged against the pole as the first approval left it: not stale, but its rejection may still have to restore it.
        await StageAsync(harness, "ord-2", "pole-1", OccupancyChangeBuilder.Add(2));
        await StageAsync(harness, "ord-2", "pole-2", OccupancyChangeBuilder.Add(1));

        var act = () => ApproveAsync(harness, "ord-2");

        (await act.Should().ThrowAsync<OccupancyApplyPendingException>()).Which.PoleFeatureIds.Should().Equal("pole-1");
        await AssertNothingCommittedAsync(harness, "ord-2", stagedSets: 2);
        (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None)).Should().BeNull("the other pole was not written either");
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle("only the first order's batch");
    }

    [Fact]
    public async Task ApproveAndCommitAsync_AnotherOrdersPole_IsApprovable_OnceTheFirstApprovalWasApplied()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await SeedOrderAsync(harness, "ord-2", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        await ReplyAppliedAsync(harness);
        await StageAsync(harness, "ord-2", "pole-1", OccupancyChangeBuilder.Add(2));

        var result = await ApproveAsync(harness, "ord-2");

        result.AwaitingApply.Should().BeTrue();
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().HaveCount(2);
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

    // ── The approval saga (XD01-383) ──────────────────────────────────────────

    [Fact]
    public async Task ApplyReply_Redelivered_WithTheSameMessageId_IsADuplicate_AndChangesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        var message = Guid.NewGuid();
        var first = await ReplyAppliedAsync(harness, message);

        var second = await ReplyAppliedAsync(harness, message);

        first.Outcome.Should().Be(OccupancyApplySagaOutcome.Committed);
        second.Outcome.Should().Be(OccupancyApplySagaOutcome.Duplicate);
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Select(entry => entry.Action)
            .Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Applied);
        (await SetsAsync(harness, "ord-1")).Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Committed);
    }

    [Fact]
    public async Task ApplyReply_ADifferentMessageForAnAlreadyCommittedSaga_IsADuplicate_AndChangesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        await ReplyAppliedAsync(harness);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        var again = await ReplyAppliedAsync(harness);

        again.Outcome.Should().Be(OccupancyApplySagaOutcome.Duplicate);
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.ActionLog.Should().HaveCount(2);
        order.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!.CompletedAt
            .Should().Be(OccupancyCommitFixtureClockNow(harness));
    }

    [Fact]
    public async Task ApplyReply_WhenTheCatalogRejectsTheBatch_RestoresEveryPoleToItsBaseline_RevertsTheSets_AndLeavesTheOrderCompletedAsReverted()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        var baseline = await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));
        await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        harness.Clock.Advance(TimeSpan.FromMinutes(3));
        var rejectedAt = harness.Clock.GetUtcNow();

        var result = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.FeatureNotFound, "Feature 'pole-2' not found for UpdateAttributes.", "pole-2");

        result.Outcome.Should().Be(OccupancyApplySagaOutcome.Reverted);
        var pole1 = (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!;
        pole1.Version.Should().Be(3, "the restore is a new version on top of the approval's");
        pole1.Entries.Should().BeEquivalentTo(baseline.Entries, "the pole holds what it held before the approval");
        var pole2 = (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None))!;
        pole2.Version.Should().Be(2);
        pole2.Entries.Should().BeEmpty("the pole had no occupancy before the approval");
        (await SetsAsync(harness, "ord-1")).Should().HaveCount(2)
            .And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Reverted && set.ClosedAt == rejectedAt);
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Status.Should().Be(ServiceOrderStatus.Completed, "a rejected approval leaves the order where it was");
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Reverted);
        order.ActionLog.Select(entry => entry.Action).Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Reverted);
        order.ActionLog[1].Comment.Should().Contain(AssetMutationBatchRejectionCodes.FeatureNotFound)
            .And.Contain("Feature 'pole-2' not found").And.Contain("pole-2");
        order.ActionLog[1].ResultingStatus.Should().BeNull();
        var saga = (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!;
        saga.State.Should().Be(OccupancyApplySagaState.Reverted);
        saga.ReasonCode.Should().Be(AssetMutationBatchRejectionCodes.FeatureNotFound);
        saga.Reason.Should().Contain("pole-2");
        saga.ConflictingFeatureIds.Should().Equal("pole-2");
    }

    [Fact]
    public async Task ApplyReply_WhenAPoleWasRewrittenSinceTheApproval_RevertsWithoutThrowing_KeepsThatPole_RestoresTheOthers_AndNamesItOnTheOrder()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));
        await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        var approved = (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!;
        var later = await OccupyAsync(harness, "pole-1", [.. approved.Entries, Entry(harness, "pole-1", 3, OccupantTwo)]);

        var result = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.FeatureNotFound, "Feature 'pole-2' not found for UpdateAttributes.", "pole-2");

        result.Outcome.Should().Be(OccupancyApplySagaOutcome.Reverted);
        result.Saga!.UnrestoredFeatureIds.Should().Equal("pole-1");
        var saga = (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!;
        saga.State.Should().Be(OccupancyApplySagaState.Reverted);
        saga.UnrestoredFeatureIds.Should().Equal("pole-1");
        var pole1 = (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!;
        pole1.Version.Should().Be(later.Version, "the compensation did not touch the pole the later change wrote");
        pole1.Entries.Should().BeEquivalentTo(later.Entries);
        var pole2 = (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None))!;
        pole2.Entries.Should().BeEmpty("the pole nobody else wrote is restored to its baseline");
        pole2.Version.Should().Be(2);
        (await SetsAsync(harness, "ord-1")).Should().HaveCount(2).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Reverted);
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Reverted)
            .And.Contain(OccupancyApplySagaAttributes.UnrestoredKey, "pole-1");
        OccupancyApplySagaAttributes.IsAwaitingApply(order.Attributes).Should().BeFalse();
        order.ActionLog.Select(entry => entry.Action).Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Reverted);
        order.ActionLog[1].Comment.Should().Contain(AssetMutationBatchRejectionCodes.FeatureNotFound)
            .And.Contain("The approval was compensated except on pole(s) pole-1: a later change superseded the approval there, so their occupancy was kept.")
            .And.Contain("The occupancy of the other pole(s) was restored and the changes must be staged again.");
        order.ActionLog[1].ResultingStatus.Should().BeNull();
    }

    [Fact]
    public async Task ApplyReply_ARejectionRedeliveredAfterAConflictingRevert_IsADuplicate_AndChangesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        var later = await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 4, OccupantTwo));
        var message = Guid.NewGuid();
        await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.ValidationFailed, "bad attribute", "pole-1", message);
        var before = (await harness.Orders.GetByIdAsync("ord-1"))!;

        var sameMessage = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.ValidationFailed, "bad attribute", "pole-1", message);
        var otherMessage = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.ValidationFailed, "bad attribute", "pole-1");

        sameMessage.Outcome.Should().Be(OccupancyApplySagaOutcome.Duplicate);
        otherMessage.Outcome.Should().Be(OccupancyApplySagaOutcome.Duplicate);
        var pole = (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!;
        pole.Version.Should().Be(later.Version);
        pole.Entries.Should().BeEquivalentTo(later.Entries);
        var after = (await harness.Orders.GetByIdAsync("ord-1"))!;
        after.ActionLog.Should().HaveCount(before.ActionLog.Count);
        after.Attributes.Should().BeEquivalentTo(before.Attributes);
        (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!.UnrestoredFeatureIds.Should().Equal("pole-1");
    }

    [Fact]
    public async Task ApplyReply_WhenEveryPoleWasRewrittenSinceTheApproval_RevertsKeepingAll_NamesThemInOrdinalOrder_AndRestoresNone()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.Add(1));
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        var later2 = await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 5, OccupantTwo));
        var later1 = await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 5, OccupantTwo));

        var result = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.FeatureNotFound, "gone", "pole-1");

        result.Outcome.Should().Be(OccupancyApplySagaOutcome.Reverted);
        result.Saga!.UnrestoredFeatureIds.Should().Equal("pole-1", "pole-2");
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().BeEquivalentTo(later1.Entries);
        (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None))!.Entries.Should().BeEquivalentTo(later2.Entries);
        (await SetsAsync(harness, "ord-1")).Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Reverted);
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.UnrestoredKey, "pole-1,pole-2");
        order.ActionLog[1].Comment.Should().Contain("compensated except on pole(s) pole-1, pole-2");
    }

    [Fact]
    public async Task ApplyReply_ARevertThatRestoresEveryPole_LeavesNoUnrestoredAttribute_AndLogsTheOriginalText()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);

        var result = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.Unsupported, "not supported");

        result.Outcome.Should().Be(OccupancyApplySagaOutcome.Reverted);
        result.Saga!.UnrestoredFeatureIds.Should().BeEmpty();
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Attributes.Should().NotContainKey(OccupancyApplySagaAttributes.UnrestoredKey);
        order.ActionLog[1].Comment.Should().EndWith("The approval was compensated: the occupancy of 1 pole(s) was restored and the changes must be staged again.")
            .And.NotContain("except on");
    }

    [Fact]
    public async Task ApplyReply_AnAppliedAnswerAfterAConflictingRevert_IsAppliedAfterRevert_AndChangesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        var later = await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 4, OccupantTwo));
        await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.Unsupported, "not supported", "pole-1");
        var before = (await harness.Orders.GetByIdAsync("ord-1"))!;

        var late = await ReplyAppliedAsync(harness);

        late.Outcome.Should().Be(OccupancyApplySagaOutcome.AppliedAfterRevert);
        var after = (await harness.Orders.GetByIdAsync("ord-1"))!;
        after.Attributes.Should().BeEquivalentTo(before.Attributes);
        after.ActionLog.Should().HaveCount(before.ActionLog.Count);
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().BeEquivalentTo(later.Entries);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_AfterAConflictingRevert_IsNotBlocked_AndClearsTheStaleUnrestoredAttribute()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 4, OccupantTwo));
        await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.FeatureNotFound, "gone", "pole-1");
        (await harness.Orders.GetByIdAsync("ord-1"))!.Attributes.Should().ContainKey(OccupancyApplySagaAttributes.UnrestoredKey);
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));

        var result = await ApproveAsync(harness, "ord-1");

        result.AssetMutationBatchId.Should().Be(OccupancyCommitService.AssetMutationBatchId("ord-1", attempt: 2));
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        OccupancyApplySagaAttributes.IsAwaitingApply(order.Attributes).Should().BeTrue();
        order.Attributes.Should().NotContainKey(OccupancyApplySagaAttributes.UnrestoredKey, "it describes the latest saga only");
    }

    [Fact]
    public async Task ApplyReply_ARejectionDeliveredTwice_RevertsOnce()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        var message = Guid.NewGuid();
        await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.ValidationFailed, "bad attribute", messageId: message);

        var sameMessage = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.ValidationFailed, "bad attribute", messageId: message);
        var otherMessage = await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.ValidationFailed, "bad attribute");

        sameMessage.Outcome.Should().Be(OccupancyApplySagaOutcome.Duplicate);
        otherMessage.Outcome.Should().Be(OccupancyApplySagaOutcome.Duplicate);
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(2, "restored once");
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Select(entry => entry.Action)
            .Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Reverted);
    }

    [Fact]
    public async Task ApplyReply_AnAppliedAnswerAfterTheSagaReverted_IsAppliedAfterRevert_AndChangesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.Unsupported, "not supported");

        var late = await ReplyAppliedAsync(harness);

        late.Outcome.Should().Be(OccupancyApplySagaOutcome.AppliedAfterRevert);
        (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!.State.Should().Be(OccupancyApplySagaState.Reverted);
        var order = (await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        order.Attributes.Should().Contain(OccupancyApplySagaAttributes.StateKey, OccupancyApplySagaAttributes.Reverted);
        order.ActionLog.Should().HaveCount(2);
        (await SetsAsync(harness, "ord-1")).Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Reverted);
    }

    [Fact]
    public async Task ApplyReply_FromAnotherVertical_OrForAnUnknownBatch_IsIgnored()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        var batch = (await harness.StagedBatchesAsync(CancellationToken.None)).Single().Batch;
        var foreign = AppliedFor(harness, batch) with { SourceVerticalId = "field-service" };
        var unknown = AppliedFor(harness, batch) with { BatchId = Guid.NewGuid() };
        var rejectedForeign = AssetMutationBatchRejectedEvent.For(batch, AssetMutationBatchRejectionCodes.InvalidBatch, "x", [], harness.Clock)
            with { SourceVerticalId = "field-service" };

        var fromAnotherVertical = await harness.Sagas.HandleAppliedAsync(foreign, ContextFor(IntegrationEventTypes.AssetMutationBatchAppliedV1), CancellationToken.None);
        var unknownBatch = await harness.Sagas.HandleAppliedAsync(unknown, ContextFor(IntegrationEventTypes.AssetMutationBatchAppliedV1), CancellationToken.None);
        var rejectedFromAnotherVertical = await harness.Sagas.HandleRejectedAsync(rejectedForeign, ContextFor(IntegrationEventTypes.AssetMutationBatchRejectedV1), CancellationToken.None);

        fromAnotherVertical.Outcome.Should().Be(OccupancyApplySagaOutcome.Ignored);
        unknownBatch.Outcome.Should().Be(OccupancyApplySagaOutcome.Ignored);
        rejectedFromAnotherVertical.Outcome.Should().Be(OccupancyApplySagaOutcome.Ignored);
        (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!.State.Should().Be(OccupancyApplySagaState.Pending);
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_AfterARejectedApproval_StagesAnAttemptTwoBatch_WithItsOwnId()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.FeatureNotFound, "gone", "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(3));

        var result = await ApproveAsync(harness, "ord-1");

        var second = OccupancyCommitService.AssetMutationBatchId("ord-1", attempt: 2);
        second.Should().NotBe(OccupancyCommitService.AssetMutationBatchId("ord-1"));
        result.AssetMutationBatchId.Should().Be(second);
        var staged = await harness.StagedBatchesAsync(CancellationToken.None);
        staged.Should().HaveCount(2);
        staged[1].Batch.BatchId.Should().Be(second);
        staged[1].CausationId.Should().Be(OccupancyCommitService.CausationId("ord-1", 2)).And.Be("occupancy-commit:ord-1:2");
        var saga = (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!;
        saga.Attempt.Should().Be(2);
        saga.BatchId.Should().Be(second);
        saga.State.Should().Be(OccupancyApplySagaState.Pending);
        (await harness.SagaReader.FindAsync(OccupancyCommitService.AssetMutationBatchId("ord-1"), CancellationToken.None))!
            .State.Should().Be(OccupancyApplySagaState.Reverted, "the first attempt's saga stays on file as it ended");
        (await harness.Orders.GetByIdAsync("ord-1"))!.Attributes.Should().Contain(OccupancyApplySagaAttributes.BatchIdKey, second.ToString("D"));
    }

    [Fact]
    public async Task RetryAsync_RestagesTheSameBatchUnchanged_CountsTheRetry_AndRestartsTheTimeout()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        var original = (await harness.StagedBatchesAsync(CancellationToken.None)).Single();
        harness.Clock.Advance(TimeSpan.FromMinutes(20));
        var retriedAt = harness.Clock.GetUtcNow();

        var view = await harness.Sagas.RetryAsync("ord-1", Approver(harness), CancellationToken.None);

        view.State.Should().Be(OccupancyApplySagaState.Pending);
        view.RetryCount.Should().Be(1);
        view.BatchId.Should().Be(original.Batch.BatchId);
        view.LastRequestedAt.Should().Be(retriedAt, "the timeout restarts from the retry");
        view.TimedOutAt.Should().BeNull();
        var staged = await harness.StagedBatchesAsync(CancellationToken.None);
        staged.Should().HaveCount(2);
        staged[1].Batch.Should().BeEquivalentTo(original.Batch, "the retry is the same batch, so the kernel applies it at most once");
        staged[1].MessageType.Should().Be(IntegrationEventTypes.AssetMutationBatchRequestedV1);
        staged[1].PartitionKey.Should().Be("ord-1");
        var saga = (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!;
        saga.RetryCount.Should().Be(1);
        saga.LastRequestedAt.Should().Be(retriedAt);
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Select(entry => entry.Action)
            .Should().Equal(OrderActionType.Approve, OccupancyApplyActions.Retried);

        (await harness.Sagas.RetryAsync("ord-1", Approver(harness), CancellationToken.None)).RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task RetryAsync_WithNoPendingSaga_ThrowsOccupancyApplyNotPending_AndStagesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");

        var neverApproved = () => harness.Sagas.RetryAsync("ord-1", Approver(harness), CancellationToken.None);
        (await neverApproved.Should().ThrowAsync<OccupancyApplyNotPendingException>()).Which.State.Should().BeNull();

        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
        await ReplyAppliedAsync(harness);
        var settled = () => harness.Sagas.RetryAsync("ord-1", Approver(harness), CancellationToken.None);
        (await settled.Should().ThrowAsync<OccupancyApplyNotPendingException>()).Which.State.Should().Be(OccupancyApplySagaState.Committed);

        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task RetryAsync_ForAnUnknownOrder_OrACallerTheRulesRefuse_OrAnotherOrganization_IsRefused_AndStagesNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        var memberWithoutRole = new WorkflowPrincipal("crew-9", harness.OwnerOrganizationId.ToString(), [], [], []);
        var otherAdministrator = new WorkflowPrincipal("admin-x", Guid.NewGuid().ToString(), ["Administrator"], [], []);

        var unknown = () => harness.Sagas.RetryAsync("ord-missing", Approver(harness), CancellationToken.None);
        var byMember = () => harness.Sagas.RetryAsync("ord-1", memberWithoutRole, CancellationToken.None);
        var byOutsider = () => harness.Sagas.RetryAsync("ord-1", otherAdministrator, CancellationToken.None);

        await unknown.Should().ThrowAsync<KeyNotFoundException>();
        (await byMember.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.IsOutsideOrganization.Should().BeFalse();
        (await byOutsider.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.IsOutsideOrganization.Should().BeTrue();
        (await harness.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle();
        (await harness.SagaReader.FindLatestForOrderAsync("ord-1", CancellationToken.None))!.RetryCount.Should().Be(0);
    }

    // ── Saga helpers ──────────────────────────────────────────────────────────

    /// <summary>A completed order of one pole with a staged Add on it, approved: its saga pending.</summary>
    protected static async Task SeedAndApproveAsync(OccupancyCommitContractHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ApproveAsync(harness, "ord-1");
    }

    private static DateTimeOffset OccupancyCommitFixtureClockNow(OccupancyCommitContractHarness harness) =>
        harness.Clock.GetUtcNow() - TimeSpan.FromMinutes(5);

    private static IntegrationMessageContext ContextFor(string messageType, Guid? messageId = null) =>
        new IntegrationMessageContextBuilder()
            .WithMessageId(messageId ?? Guid.NewGuid())
            .WithMessageType(messageType)
            .Build();

    private static AssetBatchAppliedEvent AppliedFor(OccupancyCommitContractHarness harness, AssetMutationBatchRequestedEvent batch) =>
        AssetBatchAppliedEvent.Create(
            batch.BatchId, batch.CatalogInstanceId, batch.OrganizationId, batch.RequestedByService, "kernel", [], harness.Clock,
            sourceOrderId: batch.SourceOrderId, sourceOrderTypeId: batch.SourceOrderTypeId, correlationId: batch.CorrelationId,
            sourceVerticalId: batch.SourceVerticalId);

    /// <summary>Delivers the kernel's Applied answer to the latest staged batch.</summary>
    protected static async Task<OccupancyApplySagaResult> ReplyAppliedAsync(OccupancyCommitContractHarness harness, Guid? messageId = null)
    {
        ArgumentNullException.ThrowIfNull(harness);
        var batch = (await harness.StagedBatchesAsync(CancellationToken.None))[^1].Batch;
        return await harness.Sagas.HandleAppliedAsync(
            AppliedFor(harness, batch), ContextFor(IntegrationEventTypes.AssetMutationBatchAppliedV1, messageId), CancellationToken.None);
    }

    /// <summary>Delivers the kernel's Rejected answer to the latest staged batch.</summary>
    protected static async Task<OccupancyApplySagaResult> ReplyRejectedAsync(
        OccupancyCommitContractHarness harness, string reasonCode, string reason, string? conflicting = null, Guid? messageId = null)
    {
        ArgumentNullException.ThrowIfNull(harness);
        var batch = (await harness.StagedBatchesAsync(CancellationToken.None))[^1].Batch;
        var rejected = AssetMutationBatchRejectedEvent.For(batch, reasonCode, reason, conflicting is null ? [] : [conflicting], harness.Clock);
        return await harness.Sagas.HandleRejectedAsync(
            rejected, ContextFor(IntegrationEventTypes.AssetMutationBatchRejectedV1, messageId), CancellationToken.None);
    }

    // ── Assertion helpers ─────────────────────────────────────────────────────

    /// <summary>The order is still completed, unlogged, its sets all staged, and no batch was staged.</summary>
    protected static async Task AssertNothingCommittedAsync(OccupancyCommitContractHarness harness, string orderId, int stagedSets)
    {
        ArgumentNullException.ThrowIfNull(harness);

        var order = (await harness.Orders.GetByIdAsync(orderId))!;
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        order.ActionLog.Should().BeEmpty();
        OccupancyApplySagaAttributes.IsAwaitingApply(order.Attributes).Should().BeFalse();
        (await harness.SagaReader.FindLatestForOrderAsync(orderId, CancellationToken.None)).Should().BeNull("no saga began");
        (await SetsAsync(harness, orderId)).Should().HaveCount(stagedSets).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Staged);
        (await harness.StagedBatchesAsync(CancellationToken.None)).Where(staged => string.Equals(staged.Batch.SourceOrderId, orderId, StringComparison.Ordinal)).Should().BeEmpty();
    }

    private static Dictionary<string, string> Attributes(OccupancyClass occupancyClass, int occupied, bool overCapacity, bool unidentified) => new(StringComparer.Ordinal)
    {
        [OccupancyAssetAttributes.ClassKey] = occupancyClass.ToString(),
        [OccupancyAssetAttributes.OccupiedCountKey] = occupied.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [OccupancyAssetAttributes.OverCapacityKey] = overCapacity ? "true" : "false",
        [OccupancyAssetAttributes.UnidentifiedKey] = unidentified ? "true" : "false",
    };
}
