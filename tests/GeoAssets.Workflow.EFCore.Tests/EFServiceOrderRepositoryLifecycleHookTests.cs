using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// XD01-362: <see cref="IOrderLifecycleHook"/>s of <see cref="EFServiceOrderRepository"/> on a real PostgreSQL — they run in
/// the transition's transaction, see the instant its targets are released at, and a failing one rolls everything back.
/// </summary>
public sealed class EFServiceOrderRepositoryLifecycleHookTests : IAsyncDisposable
{
    private const string DatabaseName = "order_lifecycle_hooks";

    private static readonly Guid Tenant = ServiceOrderTargetBuilder.DefaultOrganizationId;
    private static readonly DateTimeOffset Created = new(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClosedAt = new(2026, 11, 18, 17, 30, 0, TimeSpan.Zero);

    private readonly DisposableGroup _scope = new();
    private readonly OrderTypeRegistry _orderTypes = new();
    private readonly FakeTimeProvider _clock = new(Created);
    private string _connectionString = string.Empty;

    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    private sealed class ProbeHook(ServiceOrderDbContext db) : IOrderLifecycleHook
    {
        public List<OrderTransitionContext> Seen { get; } = [];

        public bool HadTransaction { get; private set; }

        public Task OnTransitionedAsync(OrderTransitionContext context, CancellationToken cancellationToken)
        {
            Seen.Add(context);
            HadTransaction = db.Database.CurrentTransaction is not null;
            return Task.CompletedTask;
        }
    }

    /// <summary>Writes past the change tracker — straight to the database, in the transition's transaction — and then fails.</summary>
    private sealed class WriteThenFailHook(IOccupancyChangeSetWriter changeSets) : IOrderLifecycleHook
    {
        public async Task OnTransitionedAsync(OrderTransitionContext context, CancellationToken cancellationToken)
        {
            (await changeSets.DiscardAllAsync(context.Order.Id, cancellationToken)).Should().BeGreaterThan(0, "the write must have happened");
            throw new InvalidOperationException("hook failed");
        }
    }

    private sealed class Stack
    {
        public required ServiceOrderDbContext Context { get; init; }

        public required EFOccupancyChangeSetRepository ChangeSets { get; init; }

        public required EFServiceOrderTargetRepository Targets { get; init; }

        public required Func<IEnumerable<IOrderLifecycleHook>?, EFServiceOrderRepository> Orders { get; init; }
    }

    private async Task<Stack> NewStackAsync()
    {
        _connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await PostgresTargetStoreDatabase.ClearAsync(_connectionString);
        var context = _scope.Add(PostgresTargetStoreDatabase.NewContext(_connectionString, new FakeTenantContext(Tenant)));
        return new Stack
        {
            Context = context,
            ChangeSets = new EFOccupancyChangeSetRepository(context, _clock),
            Targets = new EFServiceOrderTargetRepository(context, _orderTypes, _clock),
            Orders = hooks => new EFServiceOrderRepository(context, orderTypeRegistry: _orderTypes, timeProvider: _clock, hooks: hooks),
        };
    }

    /// <summary>Order <c>ord-1</c> with one target and one staged change set on it.</summary>
    private static async Task SeedAsync(Stack stack)
    {
        await stack.Orders(null).AddAsync(new ServiceOrder
        {
            Id = "ord-1", Title = "ord-1", OrderTypeId = "inspection", OrganizationId = Tenant, CreatedBy = "u1", CreatedAt = Created.UtcDateTime,
        });
        var target = new ServiceOrderTargetBuilder().WithId("ord-1:pole-1").WithOrderId("ord-1").WithFeatureId("pole-1").WithOrganizationId(Tenant).Build();
        await stack.Targets.AddRangeAsync("ord-1", [target], CancellationToken.None);
        await stack.ChangeSets.StageAsync(
            OccupancyChangeSet.Stage("ord-1", target.Id, "pole-1", Tenant, [OccupancyChangeBuilder.Add(2)], null, "crew-1", Created),
            CancellationToken.None);
    }

    private async Task CancelAsync(EFServiceOrderRepository orders)
    {
        _clock.SetUtcNow(ClosedAt);
        var order = (ServiceOrder)(await orders.GetByIdAsync("ord-1"))!;
        order.Status = ServiceOrderStatus.Cancelled;
        await orders.UpdateAsync(order);
    }

    private async Task<(string Status, DateTime? ReleasedAt, string SetStatus)> StoredAsync()
    {
        await using var verify = PostgresTargetStoreDatabase.NewContext(_connectionString, new FakeTenantContext(Tenant));
        var status = await verify.ServiceOrders.AsNoTracking().Select(o => o.Status).SingleAsync();
        var released = await verify.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking().Select(t => t.ReleasedAt).SingleAsync();
        var set = await verify.OccupancyChangeSets.AsNoTracking().Select(s => s.Status).SingleAsync();
        return (status, released, set);
    }

    [Fact]
    public async Task ClosingAnOrder_DiscardsStagedChangeSets_InTheSameTransaction()
    {
        var stack = await NewStackAsync();
        await SeedAsync(stack);
        var probe = new ProbeHook(stack.Context);
        var saves = new List<string[]>();
        stack.Context.SavingChanges += (_, _) => saves.Add(
            [.. stack.Context.ChangeTracker.Entries().Where(e => e.State == EntityState.Modified).Select(e => e.Metadata.ClrType.Name).Order(StringComparer.Ordinal)]);

        await CancelAsync(stack.Orders([new OccupancyReleaseLifecycleHook(stack.ChangeSets), probe]));

        probe.HadTransaction.Should().BeTrue("hooks run inside the transition's transaction");
        saves.Should().ContainSingle().Which.Should().Equal(
            nameof(OccupancyChangeSetRecord), nameof(ServiceOrderRecord), nameof(ServiceOrderTargetRecord));
        var stored = await StoredAsync();
        stored.Status.Should().Be(ServiceOrderStatus.Cancelled);
        stored.ReleasedAt.Should().Be(ClosedAt.UtcDateTime);
        stored.SetStatus.Should().Be("Discarded");
    }

    [Fact]
    public async Task HookFailure_RollsBackTheTransition_AndItsOwnWrites()
    {
        var stack = await NewStackAsync();
        await SeedAsync(stack);
        var orders = stack.Orders([new OccupancyReleaseLifecycleHook(stack.ChangeSets), new WriteThenFailHook(stack.ChangeSets)]);

        var act = () => CancelAsync(orders);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("hook failed");
        var stored = await StoredAsync();
        stored.Status.Should().Be(ServiceOrderStatus.Draft, "the transition rolled back");
        stored.ReleasedAt.Should().BeNull("the targets were not released");
        stored.SetStatus.Should().Be("Staged", "the hook's own write — tracked and direct — is gone");
        stack.Context.ChangeTracker.Entries<OccupancyChangeSetRecord>().Should().OnlyContain(e => e.State == EntityState.Unchanged);
        stack.Context.ChangeTracker.Entries<ServiceOrderTargetRecord>().Should().OnlyContain(e => e.State == EntityState.Unchanged);
    }

    [Fact]
    public async Task NoHooks_TransitionSavesWithoutATransaction()
    {
        var stack = await NewStackAsync();
        await SeedAsync(stack);
        var hadTransaction = new List<bool>();
        stack.Context.SavingChanges += (_, _) => hadTransaction.Add(stack.Context.Database.CurrentTransaction is not null);

        await CancelAsync(stack.Orders(null));

        hadTransaction.Should().Equal(false);
        var stored = await StoredAsync();
        stored.Status.Should().Be(ServiceOrderStatus.Cancelled);
        stored.ReleasedAt.Should().Be(ClosedAt.UtcDateTime);
        stored.SetStatus.Should().Be("Staged", "without the occupancy hook nothing discards the set");
    }

    [Fact]
    public async Task HooksSeeTheReleaseInstant()
    {
        var stack = await NewStackAsync();
        await SeedAsync(stack);
        var probe = new ProbeHook(stack.Context);

        await CancelAsync(stack.Orders([probe]));

        var seen = probe.Seen.Should().ContainSingle().Subject;
        seen.OccurredAt.Should().Be(ClosedAt);
        seen.FromStatus.Should().Be(ServiceOrderStatus.Draft);
        seen.ToStatus.Should().Be(ServiceOrderStatus.Cancelled);
        (await StoredAsync()).ReleasedAt.Should().Be(seen.OccurredAt.UtcDateTime);
    }
}
