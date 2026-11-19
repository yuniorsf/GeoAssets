using FluentAssertions;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Runs the shared <see cref="SharedUseDispatchContractTests"/> against
/// <see cref="SharedUseDispatchService"/> on the in-memory TestKit stores. The same suite runs on
/// EF Core over SQLite and over PostgreSQL in GeoAssets.Workflow.EFCore.Tests, which is also where
/// "all or nothing" is asserted — the in-memory unit of work has nothing to roll back.
/// </summary>
public sealed class InMemorySharedUseDispatchContractTests : SharedUseDispatchContractTests
{
    protected override Task<SharedUseDispatchContractHarness> CreateHarnessAsync() =>
        Task.FromResult(new SharedUseDispatchFixture().ToHarness());

    [Fact]
    public async Task DispatchAsync_OnTheInMemoryStores_RunsItsWritesInExactlyOneUnitOfWork_AndAReplayInNone()
    {
        var fixture = new SharedUseDispatchFixture();
        await using var harness = fixture.ToHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));

        await DispatchAsync(harness, "ord-1");
        await DispatchAsync(harness, "ord-1");

        fixture.UnitOfWork.Started.Should().Be(1, "the second call found nothing to create and wrote nothing");
        fixture.UnitOfWork.Completed.Should().Be(1);
    }
}
