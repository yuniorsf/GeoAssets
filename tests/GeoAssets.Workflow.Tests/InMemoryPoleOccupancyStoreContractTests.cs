using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests;

/// <summary>
/// Runs the shared <see cref="PoleOccupancyStoreContractTests"/> against the TestKit's
/// <see cref="InMemoryPoleOccupancyStore"/>, so services tested on that fake are tested on
/// something that behaves like the EF Core store (which runs the same suite in
/// GeoAssets.Workflow.EFCore.Tests, on SQLite and on PostgreSQL).
/// </summary>
public sealed class InMemoryPoleOccupancyStoreContractTests : PoleOccupancyStoreContractTests
{
    private static readonly Guid Owner = PoleOccupancyEntryBuilder.DefaultPoleOwnerOrganizationId;

    protected override Task<PoleOccupancyStoreContractHarness> CreateHarnessAsync()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryPoleOccupancyStore(timeProvider: clock).ForOrganization(Owner);

        return Task.FromResult(new PoleOccupancyStoreContractHarness(
            store,
            store,
            Owner,
            clock,
            organizationId =>
            {
                var view = store.ForOrganization(organizationId);
                return new PoleOccupancyStoreView(view, view);
            }));
    }

    /// <summary>
    /// The fake needs neither tenant nor clock to be usable: without them it is scoped to
    /// <see cref="Guid.Empty"/>, like a host with no tenant registered. Declaring a test
    /// directly on the class also keeps it public and therefore discoverable by xUnit.
    /// </summary>
    [Fact]
    public async Task Store_WithNoTenantOrClock_IsScopedToTheEmptyOrganization_AndSnapshotShowsEveryOrganizationsPoles()
    {
        var store = new InMemoryPoleOccupancyStore();
        var other = store.ForOrganization(Owner);
        await store.ReplaceEntriesAsync(
            "pole-1", 0, [new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(Guid.Empty).Build()], CancellationToken.None);
        await other.ReplaceEntriesAsync(
            "pole-1", 0, [new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(Owner).Build()], CancellationToken.None);

        var mine = await store.GetForPoleAsync("pole-1", CancellationToken.None);

        mine!.PoleOwnerOrganizationId.Should().Be(Guid.Empty);
        mine.UpdatedAt.Should().BeCloseTo(TimeProvider.System.GetUtcNow(), TimeSpan.FromMinutes(1));
        store.Snapshot().Select(pole => pole.PoleOwnerOrganizationId).Should().BeEquivalentTo([Guid.Empty, Owner]);
    }
}
