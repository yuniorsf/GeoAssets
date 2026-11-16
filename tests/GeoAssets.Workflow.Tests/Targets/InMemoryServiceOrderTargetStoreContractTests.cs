using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// Runs the shared <see cref="ServiceOrderTargetStoreContractTests"/> against the TestKit's
/// <see cref="InMemoryServiceOrderTargetStore"/>, so services tested on that fake are tested on
/// something that behaves like the EF Core store (which runs the same suite in
/// GeoAssets.Workflow.EFCore.Tests, on SQLite and on PostgreSQL).
/// </summary>
public sealed class InMemoryServiceOrderTargetStoreContractTests : ServiceOrderTargetStoreContractTests
{
    private static readonly Guid Tenant = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override Task<TargetStoreContractHarness> CreateHarnessAsync()
    {
        var orders = new FakeServiceOrderRepository();
        var orderTypes = new OrderTypeRegistry();
        var store = new InMemoryServiceOrderTargetStore(orders, orderTypes, new FixedTenantContext(Tenant));

        return Task.FromResult(new TargetStoreContractHarness(store, store, orders, orderTypes, Tenant));
    }

    /// <summary>
    /// The fake needs no registry or tenant to be usable: without them every usage reports no
    /// exclusivity and the default order graph decides what is closed. Declaring a test directly
    /// on the class also keeps it public and therefore discoverable by xUnit.
    /// </summary>
    [Fact]
    public async Task Store_WithOnlyAnOrderReader_UsesTheDefaultGraphAndNoExclusivity()
    {
        var orders = new FakeServiceOrderRepository();
        var store = new InMemoryServiceOrderTargetStore(orders);
        await orders.AddAsync(new ServiceOrder { Id = "ord-open", OrderTypeId = "inspection" });
        await orders.AddAsync(new ServiceOrder { Id = "ord-closed", OrderTypeId = "inspection", Status = ServiceOrderStatus.Completed });
        await store.AddRangeAsync(
            "ord-open", [new ServiceOrderTargetBuilder().WithId("t1").WithOrderId("ord-open").WithOrganizationId(Guid.Empty).Build()], CancellationToken.None);
        await store.AddRangeAsync(
            "ord-closed", [new ServiceOrderTargetBuilder().WithId("t2").WithOrderId("ord-closed").WithOrganizationId(Guid.Empty).Build()], CancellationToken.None);

        var usages = await store.FindOpenUsagesAsync(["feat-100"], "ord-new", CancellationToken.None);

        usages.Should().ContainSingle().Which.Should().Be(
            new OrderTargetUsage("feat-100", "ord-open", "inspection", ServiceOrderStatus.Draft, null, OrderTargetExclusivity.None));
        store.Snapshot().Should().HaveCount(2);
    }

    private sealed class FixedTenantContext(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
