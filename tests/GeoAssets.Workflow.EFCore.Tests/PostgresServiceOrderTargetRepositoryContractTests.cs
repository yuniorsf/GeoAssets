using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="ServiceOrderTargetStoreContractTests"/> against
/// <see cref="EFServiceOrderTargetRepository"/> on a real PostgreSQL (Testcontainers) — the
/// engine production runs on, and the one where sorting, null ordering, <c>ON CONFLICT</c>,
/// <c>timestamp with time zone</c> and aggregate translation can differ from SQLite's. The
/// facts below it are the ones that need two real database sessions.
/// </summary>
public sealed class PostgresServiceOrderTargetRepositoryContractTests : ServiceOrderTargetStoreContractTests
{
    private const string DatabaseName = "target_store_contract";

    private static readonly Guid Tenant = ServiceOrderTargetBuilder.DefaultOrganizationId;
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    protected override async Task<TargetStoreContractHarness> CreateHarnessAsync()
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await PostgresTargetStoreDatabase.ClearAsync(connectionString);

        var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Tenant));
        var orderTypes = new OrderTypeRegistry();
        var clock = new FakeTimeProvider(Now);
        var store = new EFServiceOrderTargetRepository(context, orderTypes, clock);

        return new TargetStoreContractHarness(
            store,
            store,
            new EFServiceOrderRepository(context, orderTypeRegistry: orderTypes, timeProvider: clock),
            orderTypes,
            Tenant,
            context);
    }

    [Fact]
    public async Task UpdateAsync_SecondConnectionHoldingTheVersionTheFirstAlreadyUsed_ThrowsServiceOrderConcurrencyException()
    {
        await using var harness = await CreateHarnessAsync();
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await SeedOrderWithTargetAsync(harness, "ord-1", "tgt-1");
        await using var contextA = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Tenant));
        await using var contextB = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Tenant));
        var writerA = new EFServiceOrderTargetRepository(contextA);
        var writerB = new EFServiceOrderTargetRepository(contextB);

        // Both sessions read the target at version 0 before either writes.
        var readByA = (await writerA.GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None)).Items.Single();
        var readByB = (await writerB.GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None)).Items.Single();

        await writerA.UpdateAsync(readByA with { Note = "written by A" }, CancellationToken.None);
        var act = () => writerB.UpdateAsync(readByB with { Note = "written by B", Status = OrderTargetStatus.Skipped }, CancellationToken.None);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        var stored = (await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None)).Items.Single();
        stored.Note.Should().Be("written by A");
        stored.Status.Should().Be(OrderTargetStatus.Pending);
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_ManyConnectionsRacingOnOneVersion_ExactlyOneWinsAndTheRestGetAConcurrencyException()
    {
        const int Writers = 8;
        await using var harness = await CreateHarnessAsync();
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var target = await SeedOrderWithTargetAsync(harness, "ord-1", "tgt-1");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Writers).Select(async writer =>
        {
            await using var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Tenant));
            await context.Database.OpenConnectionAsync();
            await start.Task;
            try
            {
                await new EFServiceOrderTargetRepository(context).UpdateAsync(target with { Note = $"writer-{writer}" }, CancellationToken.None);
                return true;
            }
            catch (ServiceOrderConcurrencyException)
            {
                return false;
            }
        }).ToList();
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        outcomes.Count(won => won).Should().Be(1);
        var stored = (await harness.Reader.GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None)).Items.Single();
        stored.Version.Should().Be(1);
        stored.Note.Should().StartWith("writer-");
    }

    [Fact]
    public async Task AddRangeAsync_ConnectionsRacingToAddTheSameFeatures_StoreEachFeatureOnceAndNoCallFails()
    {
        const int Writers = 6;
        const int Features = 150;
        await using var harness = await CreateHarnessAsync();
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await harness.Orders.AddAsync(new ServiceOrder { Id = "ord-1", OrderTypeId = "inspection", OrganizationId = Tenant, CreatedBy = "u1", CreatedAt = Now.UtcDateTime });
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Writers).Select(async writer =>
        {
            await using var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Tenant));
            await context.Database.OpenConnectionAsync();
            var targets = Enumerable.Range(0, Features)
                .Select(feature => new ServiceOrderTargetBuilder()
                    .WithId($"w{writer}-f{feature:D3}")
                    .WithOrderId("ord-1")
                    .WithFeatureId($"feat-{feature:D3}")
                    .WithOrganizationId(Tenant)
                    .Build())
                .ToList();
            await start.Task;
            return await new EFServiceOrderTargetRepository(context).AddRangeAsync("ord-1", targets, CancellationToken.None);
        }).ToList();
        start.SetResult();
        var inserted = await Task.WhenAll(attempts);

        inserted.Sum().Should().Be(Features, "each feature is inserted by exactly one of the racing calls");
        (await harness.Reader.CountAsync("ord-1", CancellationToken.None)).Should().Be(Features);
    }

    [Fact]
    public async Task ClosingAnOrder_StampsItsTargetsWithTheClockAsUtc_AndTheOpenTargetIndexIsPartial()
    {
        await using var harness = await CreateHarnessAsync();
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await SeedOrderWithTargetAsync(harness, "ord-1", "tgt-1");
        var order = (ServiceOrder)(await harness.Orders.GetByIdAsync("ord-1"))!;
        order.Status = ServiceOrderStatus.Cancelled;

        await harness.Orders.UpdateAsync(order);

        await using var verify = PostgresTargetStoreDatabase.NewContext(connectionString);
        var releasedAt = await verify.ServiceOrderTargets.IgnoreQueryFilters().Select(t => t.ReleasedAt).SingleAsync();
        releasedAt.Should().Be(Now.UtcDateTime);
        var indexDefinition = await verify.Database
            .SqlQueryRaw<string>("""SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = 'IX_ServiceOrderTargets_FeatureId'""")
            .SingleAsync();
        indexDefinition.Should().Contain("""WHERE ("ReleasedAt" IS NULL)""");
    }

    private static async Task<ServiceOrderTarget> SeedOrderWithTargetAsync(TargetStoreContractHarness harness, string orderId, string targetId)
    {
        await harness.Orders.AddAsync(new ServiceOrder { Id = orderId, OrderTypeId = "inspection", OrganizationId = Tenant, CreatedBy = "u1", CreatedAt = Now.UtcDateTime });
        var target = new ServiceOrderTargetBuilder().WithId(targetId).WithOrderId(orderId).WithOrganizationId(Tenant).Build();
        await harness.Writer.AddRangeAsync(orderId, [target], CancellationToken.None);
        return target;
    }
}
