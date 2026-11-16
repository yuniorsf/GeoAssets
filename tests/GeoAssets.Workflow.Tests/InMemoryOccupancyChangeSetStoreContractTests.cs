using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests;

/// <summary>
/// Runs the shared <see cref="OccupancyChangeSetStoreContractTests"/> against the TestKit's
/// <see cref="InMemoryOccupancyChangeSetStore"/>, so services tested on that fake are tested on
/// something that behaves like the EF Core store (which runs the same suite in
/// GeoAssets.Workflow.EFCore.Tests, on SQLite and on PostgreSQL).
/// </summary>
public sealed class InMemoryOccupancyChangeSetStoreContractTests : OccupancyChangeSetStoreContractTests
{
    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override Task<OccupancyChangeSetStoreContractHarness> CreateHarnessAsync()
    {
        var orders = new FakeServiceOrderRepository();
        var orderTypes = new OrderTypeRegistry();
        var targets = new InMemoryServiceOrderTargetStore(orders, orderTypes, new FixedTenantContext(Owner));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryOccupancyChangeSetStore(targets, orders, orderTypes, new FixedTenantContext(Owner), clock);

        return Task.FromResult(new OccupancyChangeSetStoreContractHarness(
            store,
            store,
            Owner,
            clock,
            orders,
            targets,
            organizationId =>
            {
                var view = store.ForOrganization(organizationId);
                return new OccupancyChangeSetStoreView(view, view);
            }));
    }

    /// <summary>
    /// What only the fake offers, for the tests of services built on it: a released target on an
    /// order that is still open, a set seeded in a status nothing can produce yet, and a look at
    /// every organization's sets. Declaring a test directly on the class also keeps it public and
    /// therefore discoverable by xUnit.
    /// </summary>
    [Fact]
    public async Task Fake_CanMarkATargetReleased_SeedASetInAnyStatus_AndShowEveryOrganizationsSets()
    {
        await using var harness = await CreateHarnessAsync();
        var store = (InMemoryOccupancyChangeSetStore)harness.Writer;
        var target = await SeedTargetAsync(harness, "ord-1", "pole-1");
        var committed = OccupancyChangeSet.Restore(
            Guid.NewGuid(), "ord-1", target.Id, "pole-1", Owner, [OccupancyChangeBuilder.Add(1)], 3,
            OccupancyChangeSetStatus.Committed, "crew-1", harness.Clock.GetUtcNow(), harness.Clock.GetUtcNow());

        store.Seed(committed);
        store.MarkReleased(target.Id);
        var act = () => store.StageAsync(NewSet(harness, "ord-1", "pole-1"), CancellationToken.None);

        await act.Should().ThrowAsync<OccupancyTargetReleasedException>("the target is released although its order is open");
        store.Snapshot().Should().ContainSingle().Which.Should().BeSameAs(committed);
        (await store.DiscardAllAsync("ord-1", CancellationToken.None)).Should().Be(0, "a committed set is never discarded");
        var page = await store.GetPageAsync(new OccupancyChangeSetQuery("ord-1", status: OccupancyChangeSetStatus.Committed), CancellationToken.None);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(committed.Id);
    }

    [Fact]
    public void Fake_NullReaders_ThrowArgumentNullException()
    {
        var orders = new FakeServiceOrderRepository();
        var targets = new InMemoryServiceOrderTargetStore(orders);

        ((Action)(() => _ = new InMemoryOccupancyChangeSetStore(null!, orders))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new InMemoryOccupancyChangeSetStore(targets, null!))).Should().Throw<ArgumentNullException>();
    }

    private sealed class FixedTenantContext(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
