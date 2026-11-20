using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary>
/// Runs the shared <see cref="OccupancyCommitContractTests"/> against
/// <see cref="OccupancyCommitService"/> on the in-memory TestKit stores (XD01-318). The same suite
/// runs on EF Core over SQLite and over PostgreSQL in GeoAssets.Workflow.EFCore.Tests, which is
/// also where "all or nothing" is asserted — the in-memory unit of work has nothing to roll back.
/// </summary>
public sealed class InMemoryOccupancyCommitContractTests : OccupancyCommitContractTests
{
    protected override Task<OccupancyCommitContractHarness> CreateHarnessAsync() =>
        Task.FromResult(new OccupancyCommitFixture().ToHarness());

    [Fact]
    public async Task ApproveAndCommitAsync_OnTheInMemoryStores_WritesInExactlyOneUnitOfWork_AndARefusalInNone()
    {
        var fixture = new OccupancyCommitFixture();
        await using var harness = fixture.ToHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1");
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(1));

        await ApproveAsync(harness, "ord-1");
        await ((Func<Task>)(() => ApproveAsync(harness, "ord-1"))).Should().ThrowAsync<OccupancyApplyPendingException>();

        fixture.UnitOfWork.Started.Should().Be(1, "the repeated approval was refused before any write");
        fixture.UnitOfWork.Completed.Should().Be(1);
    }

    [Fact]
    public async Task ApplyReply_TheSagasLogEntries_AreAttributedToTheSystem_NotToTheApprover()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedAndApproveAsync(harness);
        await ReplyRejectedAsync(harness, GeoAssets.Core.Messaging.Events.AssetMutationBatchRejectionCodes.InvalidBatch, "bad batch");

        var log = (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog;

        log.Select(entry => entry.ActorKind).Should().Equal(ActorKind.Human, ActorKind.System);
        log[1].PerformedBy.Should().Be("approver-1", "the entry names the approval it settles, performed by the system");
    }
}
