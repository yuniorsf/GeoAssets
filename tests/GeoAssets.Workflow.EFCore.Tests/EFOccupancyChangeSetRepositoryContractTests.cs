using FluentAssertions;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="OccupancyChangeSetStoreContractTests"/> against
/// <see cref="EFOccupancyChangeSetRepository"/> on SQLite, wired the way a request scope wires
/// it: one <see cref="ServiceOrderDbContext"/> shared with <see cref="EFServiceOrderRepository"/>
/// (which is what discards an order's staged sets when the suite closes the order) and
/// <see cref="EFServiceOrderTargetRepository"/>. See
/// <see cref="PostgresOccupancyChangeSetRepositoryContractTests"/> for the same suite on PostgreSQL.
/// </summary>
public sealed class EFOccupancyChangeSetRepositoryContractTests : OccupancyChangeSetStoreContractTests
{
    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override Task<OccupancyChangeSetStoreContractHarness> CreateHarnessAsync()
    {
        var scope = new DisposableGroup();
        var fixture = scope.Add(new SqliteFixture(new FakeTenantContext(Owner)));

        // See EFServiceOrderTargetRepositoryContractTests: SqliteFixture's AFTER UPDATE trigger
        // leaves the tracked order with a stale RowVersion, so the tracked orders are re-read
        // after each save to let one context walk an order through several states.
        var context = fixture.Context;
        context.SavedChanges += (_, _) =>
        {
            foreach (var order in context.ChangeTracker.Entries<ServiceOrderRecord>().ToList())
                order.Reload();
        };
        var orderTypes = new OrderTypeRegistry();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new EFOccupancyChangeSetRepository(context, clock);

        return Task.FromResult(new OccupancyChangeSetStoreContractHarness(
            store,
            store,
            Owner,
            clock,
            new EFServiceOrderRepository(context, orderTypeRegistry: orderTypes, timeProvider: clock, hooks: [new OccupancyReleaseLifecycleHook(store)]),
            new EFServiceOrderTargetRepository(context, orderTypes, clock),
            organizationId =>
            {
                var view = new EFOccupancyChangeSetRepository(scope.Add(fixture.NewContext(new FakeTenantContext(organizationId))), clock);
                return new OccupancyChangeSetStoreView(view, view);
            },
            scope));
    }

    /// <summary>
    /// Pins what this class exists to prove — the suite runs against the EF repository itself,
    /// through both of its ports — and, being declared directly on the class, keeps it public
    /// and discoverable by xUnit (see <see cref="TestClassDiscoverabilityTests"/>).
    /// </summary>
    [Fact]
    public async Task CreateHarnessAsync_ExposesOneEfRepositoryThroughBothPorts()
    {
        await using var harness = await CreateHarnessAsync();

        harness.Reader.Should().BeOfType<EFOccupancyChangeSetRepository>();
        harness.Writer.Should().BeSameAs(harness.Reader);
        harness.AsOrganization(OtherOrganizationId).Reader.Should().BeOfType<EFOccupancyChangeSetRepository>().And.NotBeSameAs(harness.Reader);
    }
}
