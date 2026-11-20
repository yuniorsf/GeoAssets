using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary>
/// <see cref="OccupancyCommitService"/> over the in-memory TestKit stores, scoped to one pole
/// owner (XD01-318): what the in-memory contract runner and the service's own tests are built on.
/// </summary>
internal sealed class OccupancyCommitFixture
{
    public const string CatalogInstanceId = "catalog-test";

    public static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    public OccupancyCommitFixture()
    {
        Clock = new FakeTimeProvider(Now);
        Orders = new FakeServiceOrderRepository();
        OrderTypes = new OrderTypeRegistry();
        Rules = new ServiceOrderRules(OrderTypes, timeProvider: Clock);

        var tenant = new FixedTenant(Owner);
        Targets = new InMemoryServiceOrderTargetStore(Orders, OrderTypes, tenant);
        Occupancy = new InMemoryPoleOccupancyStore(tenant, Clock);
        ChangeSets = new InMemoryOccupancyChangeSetStore(Targets, Orders, OrderTypes, tenant, Clock);
        Outbox = new RecordingOutboxWriter();
        Sagas = new InMemoryOccupancyApplySagaStore(ChangeSets, Occupancy);
        Inbox = new RecordingIdempotentInbox();
        UnitOfWork = new InMemoryWorkflowUnitOfWork();
        Options = new OrderOccupancyOptions { CatalogInstanceId = CatalogInstanceId };
        SagaService = new OccupancyApplySagaService(
            Orders, OrderTypes, Rules, Sagas, Outbox, Inbox, UnitOfWork, new TenantOrganizationScope(), Clock);
        Sut = Build();
    }

    public FakeTimeProvider Clock { get; }
    public FakeServiceOrderRepository Orders { get; }
    public OrderTypeRegistry OrderTypes { get; }
    public ServiceOrderRules Rules { get; }
    public InMemoryServiceOrderTargetStore Targets { get; }
    public InMemoryPoleOccupancyStore Occupancy { get; }
    public InMemoryOccupancyChangeSetStore ChangeSets { get; }
    public RecordingOutboxWriter Outbox { get; }
    public InMemoryOccupancyApplySagaStore Sagas { get; }
    public RecordingIdempotentInbox Inbox { get; }
    public OccupancyApplySagaService SagaService { get; }
    public InMemoryWorkflowUnitOfWork UnitOfWork { get; }
    public OrderOccupancyOptions Options { get; }
    public OccupancyCommitService Sut { get; }

    /// <summary>The service again, with any of its collaborators swapped for a test's own.</summary>
    public OccupancyCommitService Build(
        IServiceOrderRepository? orders = null,
        IServiceOrderTargetReader? targets = null,
        IPoleOccupancyReader? occupancy = null,
        IPoleOccupancyWriter? occupancyWriter = null,
        IOccupancyChangeSetReader? changeSets = null,
        IOccupancyChangeSetWriter? changeSetWriter = null,
        IOutboxWriter? outbox = null,
        IWorkflowUnitOfWork? unitOfWork = null,
        IOccupancyApplySagaStore? sagas = null) =>
        new(orders ?? Orders, OrderTypes, Rules, targets ?? Targets, occupancy ?? Occupancy, occupancyWriter ?? Occupancy,
            changeSets ?? ChangeSets, changeSetWriter ?? ChangeSets, outbox ?? Outbox, sagas ?? Sagas, unitOfWork ?? UnitOfWork, Options, Clock);

    public OccupancyCommitContractHarness ToHarness() => new(
        Sut, Orders, Targets, Targets, Occupancy, Occupancy, ChangeSets, ChangeSets, OrderTypes, Clock, Owner, CatalogInstanceId,
        _ => Task.FromResult(Batches()), SagaService, Sagas);

    /// <summary>The asset mutation batches staged so far, in the shape the contract suite reads.</summary>
    public IReadOnlyList<StagedAssetMutationBatch> Batches() =>
    [
        .. Outbox.Messages.Select(staged => new StagedAssetMutationBatch(
            staged.MessageType, staged.PartitionKey, staged.CorrelationId, staged.CausationId,
            (AssetMutationBatchRequestedEvent)staged.Message)),
    ];

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
