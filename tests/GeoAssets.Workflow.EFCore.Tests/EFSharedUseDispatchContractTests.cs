using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="SharedUseDispatchContractTests"/> against
/// <see cref="SharedUseDispatchService"/> on the EF Core stores over SQLite, all on one context
/// (XD01-316) — and adds what only a database can show: that a dispatch is all or nothing.
/// </summary>
public sealed class EFSharedUseDispatchContractTests : SharedUseDispatchContractTests
{
    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override Task<SharedUseDispatchContractHarness> CreateHarnessAsync()
    {
        var scope = new DisposableGroup();
        return Task.FromResult(NewHarness(scope).ToContractHarness(scope));
    }

    private static SharedUseDispatchEfHarness NewHarness(DisposableGroup scope, IOutboxWriter? outbox = null)
    {
        var fixture = scope.Add(new SqliteFixture(new FakeTenantContext(Owner)));
        SharedUseDispatchEfHarness.ReloadOrdersAfterEachSave(fixture.Context);
        return new SharedUseDispatchEfHarness(fixture.Context, Owner, outbox);
    }

    [Fact]
    public async Task DispatchAsync_WhenStagingANotificationFails_RollsBackEverything_ParentChildrenTargetsAndOutbox()
    {
        await using var scope = new DisposableGroup();
        var failing = new RecordingOutboxWriter();
        var ef = NewHarness(scope, failing);
        await using var harness = ef.ToContractHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne), Entry(harness, "pole-1", 2, OccupantTwo));
        // The first child is fully written before the second child's notification fails.
        failing.OnEnqueueAsync = (_, _) => failing.Messages.Count == 1
            ? throw new InvalidOperationException("outbox is down")
            : Task.CompletedTask;

        var act = () => DispatchAsync(harness, "ord-1");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("outbox is down");
        ef.Context.ChangeTracker.Entries().Should().BeEmpty("nothing of the failed unit is left to be saved later");
        (await ef.Context.ServiceOrders.IgnoreQueryFilters().AsNoTracking().Select(o => o.Id).ToListAsync())
            .Should().Equal(["ord-1"], "no child survived, not even the first");
        (await ef.Context.OrderDispatches.AsNoTracking().CountAsync()).Should().Be(0);
        (await ef.Context.OrderActionLogs.AsNoTracking().CountAsync()).Should().Be(0);
        (await ef.Context.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking().CountAsync()).Should().Be(1, "only the parent's own target");
        (await ef.Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(0);
        var parent = (await harness.Orders.GetByIdAsync("ord-1"))!;
        parent.Status.Should().Be(ServiceOrderStatus.Draft, "the parent's transition was rolled back with the rest");
        parent.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_AfterARolledBackAttempt_TheSameCallSucceeds_WithNoLeftovers()
    {
        await using var scope = new DisposableGroup();
        var flaky = new RecordingOutboxWriter();
        var ef = NewHarness(scope, flaky);
        await using var harness = ef.ToContractHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne), Entry(harness, "pole-1", 2, OccupantTwo));
        var attempts = 0;
        flaky.OnEnqueueAsync = (_, _) => ++attempts == 2 ? throw new InvalidOperationException("outbox is down") : Task.CompletedTask;
        await ((Func<Task>)(() => DispatchAsync(harness, "ord-1"))).Should().ThrowAsync<InvalidOperationException>();

        var result = await DispatchAsync(harness, "ord-1");

        result.ChildOrdersCreated.Should().Be(2);
        (await harness.Orders.GetChildrenAsync("ord-1")).Should().HaveCount(2);
        (await ef.Context.OrderDispatches.AsNoTracking().CountAsync()).Should().Be(2, "one per child, none duplicated by the failed attempt");
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_WithAStaleRowVersion_ThrowsServiceOrderConcurrencyException_AndStoresNothing()
    {
        await using var scope = new DisposableGroup();
        var ef = NewHarness(scope);
        await using var harness = ef.ToContractHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        var staleRowVersion = (await harness.Orders.GetByIdAsync("ord-1"))!.RowVersion;
        // Somebody else changes the order after the caller read it.
        var current = (ServiceOrder)(await harness.Orders.GetByIdAsync("ord-1"))!;
        current.Title = "Renamed meanwhile";
        await harness.Orders.UpdateAsync(current);

        var act = () => harness.Service.DispatchAsync("ord-1", staleRowVersion, Dispatcher(harness), CancellationToken.None);

        (await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be("ord-1");
        (await harness.Orders.GetChildrenAsync("ord-1")).Should().BeEmpty();
        (await harness.StagedNotificationsAsync(CancellationToken.None)).Should().BeEmpty();
        var parent = (await harness.Orders.GetByIdAsync("ord-1"))!;
        parent.Status.Should().Be(ServiceOrderStatus.Draft);
        parent.Title.Should().Be("Renamed meanwhile");
    }

    [Fact]
    public async Task DispatchAsync_InsideACallersTransaction_JoinsIt_AndIsUndoneWhenTheCallerRollsBack()
    {
        await using var scope = new DisposableGroup();
        var ef = NewHarness(scope);
        await using var harness = ef.ToContractHarness();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));

        await using (var outer = await ef.Context.Database.BeginTransactionAsync())
        {
            var result = await DispatchAsync(harness, "ord-1");
            result.ChildOrdersCreated.Should().Be(1);
            ef.Context.Database.CurrentTransaction.Should().BeSameAs(outer, "the dispatch neither committed nor replaced the caller's transaction");
            await outer.RollbackAsync();
        }

        ef.Context.ChangeTracker.Clear();
        (await ef.Context.ServiceOrders.IgnoreQueryFilters().AsNoTracking().CountAsync()).Should().Be(1);
        (await ef.Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
