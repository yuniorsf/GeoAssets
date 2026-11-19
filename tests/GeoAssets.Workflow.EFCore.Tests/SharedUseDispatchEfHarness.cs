using GeoAssets.Core.Messaging;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using GeoAssets.NeutralOperator.Persistence;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="SharedUseDispatchService"/> wired the way a host wires it, on one
/// <see cref="ServiceOrderDbContext"/>: the EF Core order, target and occupancy repositories, the
/// EF Core outbox and <see cref="EFWorkflowUnitOfWork"/> all share that context — which is what
/// makes a dispatch one transaction. Used by the SQLite and PostgreSQL contract runners and by
/// the persistence tests.
/// </summary>
internal sealed class SharedUseDispatchEfHarness
{
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly ProtobufIntegrationMessageSerializer Serializer = new();

    public SharedUseDispatchEfHarness(ServiceOrderDbContext context, Guid owner, IOutboxWriter? outbox = null, FakeTimeProvider? clock = null)
    {
        Context = context;
        Owner = owner;
        Clock = clock ?? new FakeTimeProvider(Now);
        OrderTypes = new OrderTypeRegistry();
        Rules = new ServiceOrderRules(OrderTypes, timeProvider: Clock);
        Orders = new EFServiceOrderRepository(context, orderTypeRegistry: OrderTypes, timeProvider: Clock);
        Targets = new EFServiceOrderTargetRepository(context, OrderTypes, Clock);
        Occupancy = new EFPoleOccupancyRepository(NeutralOperatorTestContext.Over(context), Clock);
        UnitOfWork = new EFWorkflowUnitOfWork(context);
        Service = new SharedUseDispatchService(
            Orders, OrderTypes, Rules, Targets, Targets, Occupancy,
            outbox ?? new EFOutboxStore(context, Clock, Serializer), UnitOfWork, Clock);
    }

    public ServiceOrderDbContext Context { get; }
    public Guid Owner { get; }
    public FakeTimeProvider Clock { get; }
    public OrderTypeRegistry OrderTypes { get; }
    public ServiceOrderRules Rules { get; }
    public EFServiceOrderRepository Orders { get; }
    public EFServiceOrderTargetRepository Targets { get; }
    public EFPoleOccupancyRepository Occupancy { get; }
    public EFWorkflowUnitOfWork UnitOfWork { get; }
    public SharedUseDispatchService Service { get; }

    public SharedUseDispatchContractHarness ToContractHarness(IAsyncDisposable? scope = null) => new(
        Service, Service, Orders, Targets, Targets, Occupancy, OrderTypes, Clock, Owner, StagedNotificationsAsync, scope);

    /// <summary>The outbox rows as the contract suite reads notifications, oldest first.</summary>
    public async Task<IReadOnlyList<SharedUseStagedNotification>> StagedNotificationsAsync(CancellationToken ct)
    {
        var rows = await Context.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.PartitionKey).ToListAsync(ct);
        return [.. rows.Select(ToNotification)];
    }

    /// <summary>
    /// SQLite only: <see cref="SqliteFixture"/>'s AFTER UPDATE trigger changes an order's
    /// <c>RowVersion</c> after EF has read it back, so one context could not write the same order
    /// twice — as a dispatch does (the transition, then the log entry). Re-reading the tracked
    /// orders after each save stands in for what a real row-version column returns by itself.
    /// </summary>
    public static void ReloadOrdersAfterEachSave(ServiceOrderDbContext context)
    {
        context.SavedChanges += (_, _) =>
        {
            foreach (var order in context.ChangeTracker.Entries<ServiceOrderRecord>().ToList())
                order.Reload();
        };
    }

    private static SharedUseStagedNotification ToNotification(OutboxMessageRecord row)
    {
        var notification = Serializer.Deserialize<OrderStateChangedEvent>(row.Payload);
        return new SharedUseStagedNotification(
            row.MessageType, row.PartitionKey, row.CorrelationId, row.CausationId,
            notification.OrderId, notification.OrderTypeId, notification.NewStatus, notification.PerformedBy,
            notification.OccurredAt, notification.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
