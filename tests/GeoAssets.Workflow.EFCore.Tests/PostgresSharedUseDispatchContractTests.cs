using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="SharedUseDispatchContractTests"/> against
/// <see cref="SharedUseDispatchService"/> on the EF Core stores over PostgreSQL 16
/// (Testcontainers) — and adds what needs real connections: two dispatches of one order at once.
/// </summary>
public sealed class PostgresSharedUseDispatchContractTests : SharedUseDispatchContractTests
{
    private const string DatabaseName = "shared_use_dispatch_contract";

    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override async Task<SharedUseDispatchContractHarness> CreateHarnessAsync()
    {
        var connectionString = await FreshDatabaseAsync();
        var scope = new DisposableGroup();
        var context = scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner)));
        return new SharedUseDispatchEfHarness(context, Owner).ToContractHarness(scope);
    }

    private static async Task<string> FreshDatabaseAsync()
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await using var db = PostgresTargetStoreDatabase.NewContext(connectionString);
        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "ServiceOrders", "WorkflowOutboxMessages", field_pole_occupancies CASCADE;""");
        return connectionString;
    }

    [Fact]
    public async Task DispatchAsync_ManyConnectionsDispatchingTheSameOrderAtOnce_CreateEachChildExactlyOnce_AndNoCallFails()
    {
        const int Callers = 6;
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne), Entry(harness, "pole-1", 2, OccupantTwo));
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantThree));
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var rowVersion = (await harness.Orders.GetByIdAsync("ord-1"))!.RowVersion;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = Enumerable.Range(0, Callers).Select(async _ =>
        {
            await using var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner));
            await context.Database.OpenConnectionAsync();
            var caller = new SharedUseDispatchEfHarness(context, Owner);
            RegisterTypes(caller.ToContractHarness());
            await start.Task;
            return await caller.Service.DispatchAsync("ord-1", rowVersion, Dispatcher(harness), CancellationToken.None);
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(calls);

        results.Sum(r => r.ChildOrdersCreated).Should().Be(3, "each of the three children was created by exactly one caller");
        results.Sum(r => r.NotificationsEnqueued).Should().Be(3);
        await using var verify = PostgresTargetStoreDatabase.NewContext(connectionString);
        (await verify.ServiceOrders.IgnoreQueryFilters().CountAsync(o => o.ParentOrderId == "ord-1")).Should().Be(3);
        (await verify.OrderDispatches.CountAsync()).Should().Be(3);
        (await verify.OutboxMessages.CountAsync()).Should().Be(3);
        (await verify.ServiceOrderTargets.IgnoreQueryFilters().CountAsync(t => t.ServiceOrderId != "ord-1")).Should().Be(3, "one per pole per occupant");
        (await verify.OrderActionLogs.CountAsync(a => a.ServiceOrderId == "ord-1"))
            .Should().Be(results.Count(r => r.ChildOrdersCreated > 0), "only a call that created something logs a dispatch");
    }

    [Fact]
    public async Task DispatchAsync_StoresItsInstantsAsUtc_AndTheChildUnderTheOwnersOrganization()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);

        await DispatchAsync(harness, "ord-1");

        await using var verify = PostgresTargetStoreDatabase.NewContext(connectionString);
        var childId = SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne);
        var row = await verify.Database
            .SqlQuery<string>($"""
                SELECT (o."OrganizationId"::text || '|' || to_char(o."CreatedAt" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS') || '|' || d."TargetId" || '|' || d."TargetType"::text) AS "Value"
                FROM "ServiceOrders" o JOIN "OrderDispatches" d ON d."ServiceOrderId" = o."Id"
                WHERE o."Id" = {childId}
                """)
            .SingleAsync();
        row.Should().Be($"{Owner:D}|2026-06-01T09:00:00|{OccupantOne:D}|2");
    }
}
