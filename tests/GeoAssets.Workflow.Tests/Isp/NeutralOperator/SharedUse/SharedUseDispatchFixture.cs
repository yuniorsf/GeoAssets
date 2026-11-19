using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator.SharedUse;

/// <summary>
/// <see cref="SharedUseDispatchService"/> over the in-memory TestKit stores, scoped to one pole
/// owner: what the in-memory contract runner and the service's own tests are built on.
/// </summary>
internal sealed class SharedUseDispatchFixture
{
    public static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    public SharedUseDispatchFixture()
    {
        Clock = new FakeTimeProvider(Now);
        Orders = new FakeServiceOrderRepository();
        OrderTypes = new OrderTypeRegistry();
        Rules = new ServiceOrderRules(OrderTypes, timeProvider: Clock);

        var tenant = new FixedTenant(Owner);
        Targets = new InMemoryServiceOrderTargetStore(Orders, OrderTypes, tenant);
        Occupancy = new InMemoryPoleOccupancyStore(tenant, Clock);
        Outbox = new RecordingOutboxWriter();
        UnitOfWork = new InMemoryWorkflowUnitOfWork();
        Sut = Build();
    }

    public FakeTimeProvider Clock { get; }
    public FakeServiceOrderRepository Orders { get; }
    public OrderTypeRegistry OrderTypes { get; }
    public ServiceOrderRules Rules { get; }
    public InMemoryServiceOrderTargetStore Targets { get; }
    public InMemoryPoleOccupancyStore Occupancy { get; }
    public RecordingOutboxWriter Outbox { get; }
    public InMemoryWorkflowUnitOfWork UnitOfWork { get; }
    public SharedUseDispatchService Sut { get; }

    /// <summary>The service again, with any of its collaborators swapped for a test's own.</summary>
    public SharedUseDispatchService Build(
        IServiceOrderRepository? orders = null,
        IServiceOrderTargetReader? targets = null,
        IServiceOrderTargetWriter? targetWriter = null,
        IPoleOccupancyReader? occupancy = null,
        IOutboxWriter? outbox = null,
        IWorkflowUnitOfWork? unitOfWork = null,
        ISharedUseFanOutTracer? tracer = null) =>
        new(orders ?? Orders, OrderTypes, Rules, targets ?? Targets, targetWriter ?? Targets, occupancy ?? Occupancy,
            outbox ?? Outbox, unitOfWork ?? UnitOfWork, Clock, tracer);

    public SharedUseDispatchContractHarness ToHarness() => new(
        Sut, Sut, Orders, Targets, Targets, Occupancy, OrderTypes, Clock, Owner,
        _ => Task.FromResult(Notifications()));

    /// <summary>The notifications staged so far, in the shape the contract suite reads.</summary>
    public IReadOnlyList<SharedUseStagedNotification> Notifications() =>
    [
        .. Outbox.Messages.Select(staged =>
        {
            var notification = (OrderStateChangedEvent)staged.Message;
            return new SharedUseStagedNotification(
                staged.MessageType, staged.PartitionKey, staged.CorrelationId, staged.CausationId,
                notification.OrderId, notification.OrderTypeId, notification.NewStatus, notification.PerformedBy,
                notification.OccurredAt, notification.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal));
        }),
    ];

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
