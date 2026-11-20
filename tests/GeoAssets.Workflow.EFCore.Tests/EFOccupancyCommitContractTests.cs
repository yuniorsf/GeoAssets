using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="OccupancyCommitContractTests"/> against
/// <see cref="OccupancyCommitService"/> on the EF Core stores over SQLite, all on one context
/// (XD01-318) — and adds what only a database can show: that an approval is all or nothing.
/// </summary>
public sealed class EFOccupancyCommitContractTests : OccupancyCommitContractTests
{
    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override Task<OccupancyCommitContractHarness> CreateHarnessAsync()
    {
        var scope = new DisposableGroup();
        return Task.FromResult(NewHarness(scope).ToContractHarness(scope));
    }

    private static OccupancyCommitEfHarness NewHarness(DisposableGroup scope, IOutboxWriter? outbox = null)
    {
        var fixture = scope.Add(new SqliteFixture(new FakeTenantContext(Owner)));
        SharedUseDispatchEfHarness.ReloadOrdersAfterEachSave(fixture.Context);
        return new OccupancyCommitEfHarness(fixture.Context, Owner, outbox);
    }

    /// <summary>
    /// Two poles staged; the second pole's write, the sets' status and the order's transition
    /// have all run when the batch fails to stage — and none of it is kept.
    /// </summary>
    [Fact]
    public async Task ApplyReply_TheSagasLogEntries_AreAttributedToTheSystem_NotToTheApprover()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        await ReplyRejectedAsync(harness, AssetMutationBatchRejectionCodes.InvalidBatch, "bad batch");

        var log = (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog;

        log.Select(entry => entry.ActorKind).Should().Equal(ActorKind.Human, ActorKind.System);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_WhenStagingTheBatchFails_RollsBackEverything_PolesSetsOrderAndLog()
    {
        await using var scope = new DisposableGroup();
        var failing = new RecordingOutboxWriter { OnEnqueueAsync = (_, _) => throw new InvalidOperationException("outbox is down") };
        var ef = NewHarness(scope, failing);
        await using var harness = ef.ToContractHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));
        await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.Add(1), OccupancyChangeBuilder.AddBeyondRegulatedSlots());

        var act = () => ApproveAsync(harness, "ord-1");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("outbox is down");
        ef.Context.ChangeTracker.Entries().Should().BeEmpty("nothing of the failed unit is left to be saved later");
        var pole1 = (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!;
        pole1.Version.Should().Be(PoleOccupancy.FirstVersion);
        pole1.Entries.Should().ContainSingle();
        (await harness.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None)).Should().BeNull();
        await AssertNothingCommittedAsync(harness, "ord-1", stagedSets: 2);
        (await ef.Context.ServiceOrderTargets.AsNoTracking().CountAsync(t => t.ReleasedAt != null)).Should().Be(0, "the order never closed");
    }

    [Fact]
    public async Task ApproveAndCommitAsync_AfterARolledBackAttempt_TheSameCallSucceeds_WithNoLeftovers()
    {
        await using var scope = new DisposableGroup();
        var flaky = new RecordingOutboxWriter();
        var attempts = 0;
        flaky.OnEnqueueAsync = (_, _) => ++attempts == 1 ? throw new InvalidOperationException("outbox is down") : Task.CompletedTask;
        var ef = NewHarness(scope, flaky);
        await using var harness = ef.ToContractHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));
        await ((Func<Task>)(() => ApproveAsync(harness, "ord-1"))).Should().ThrowAsync<InvalidOperationException>();

        var result = await ApproveAsync(harness, "ord-1");

        result.ChangeSetsCommitted.Should().Be(1);
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(PoleOccupancy.FirstVersion);
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_InsideACallersTransaction_JoinsIt_AndIsUndoneWhenTheCallerRollsBack()
    {
        await using var scope = new DisposableGroup();
        var ef = NewHarness(scope);
        await using var harness = ef.ToContractHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));

        await using (var outer = await ef.Context.Database.BeginTransactionAsync())
        {
            await ApproveAsync(harness, "ord-1");
            ef.Context.Database.CurrentTransaction.Should().BeSameAs(outer, "the approval neither committed nor replaced the caller's transaction");
            await outer.RollbackAsync();
        }

        ef.Context.ChangeTracker.Clear();
        (await harness.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull();
        await AssertNothingCommittedAsync(harness, "ord-1", stagedSets: 1);
    }
}
