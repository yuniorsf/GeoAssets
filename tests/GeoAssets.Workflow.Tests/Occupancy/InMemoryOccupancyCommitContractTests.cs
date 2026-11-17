using FluentAssertions;
using GeoAssets.Workflow.Occupancy;
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
        await ((Func<Task>)(() => ApproveAsync(harness, "ord-1"))).Should().ThrowAsync<InvalidServiceOrderTransitionException>();

        fixture.UnitOfWork.Started.Should().Be(1, "the repeated approval was refused before any write");
        fixture.UnitOfWork.Completed.Should().Be(1);
    }
}
