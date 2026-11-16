using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="ServiceOrderTargetStoreContractTests"/> against
/// <see cref="EFServiceOrderTargetRepository"/> on SQLite, wired the way a request scope wires
/// it: one <see cref="ServiceOrderDbContext"/> shared with <see cref="EFServiceOrderRepository"/>,
/// which is what releases an order's targets when the suite closes the order. See
/// <see cref="PostgresServiceOrderTargetRepositoryContractTests"/> for the same suite on PostgreSQL.
/// </summary>
public sealed class EFServiceOrderTargetRepositoryContractTests : ServiceOrderTargetStoreContractTests
{
    private static readonly Guid Tenant = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override Task<TargetStoreContractHarness> CreateHarnessAsync()
    {
        var fixture = new SqliteFixture(new FakeTenantContext(Tenant));

        // SqliteFixture's AFTER UPDATE trigger changes ServiceOrders.RowVersion once the UPDATE
        // statement has already returned the old value to EF, so the tracked order keeps a stale
        // token and the same context could not update one order twice. PostgreSQL's BEFORE
        // trigger has no such gap; re-reading the tracked orders after each save closes it here,
        // which the suite needs to walk an order through several states.
        var context = fixture.Context;
        context.SavedChanges += (_, _) =>
        {
            foreach (var order in context.ChangeTracker.Entries<ServiceOrderRecord>().ToList())
                order.Reload();
        };
        var orderTypes = new OrderTypeRegistry();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new EFServiceOrderTargetRepository(fixture.Context, orderTypes, clock);

        return Task.FromResult(new TargetStoreContractHarness(
            store,
            store,
            new EFServiceOrderRepository(fixture.Context, orderTypeRegistry: orderTypes, timeProvider: clock),
            orderTypes,
            Tenant,
            new DisposableScope(fixture)));
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

        harness.Reader.Should().BeOfType<EFServiceOrderTargetRepository>();
        harness.Writer.Should().BeSameAs(harness.Reader);
    }
}
