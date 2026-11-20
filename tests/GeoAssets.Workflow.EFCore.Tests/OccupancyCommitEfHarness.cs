using GeoAssets.Core.Messaging;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using GeoAssets.NeutralOperator.Persistence;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="OccupancyCommitService"/> wired the way a host wires it, on one
/// <see cref="ServiceOrderDbContext"/> (XD01-318): the EF Core order, target, occupancy and
/// change-set repositories, the EF Core outbox and <see cref="EFWorkflowUnitOfWork"/> all share
/// that context — which is what makes an approval one transaction. Used by the SQLite and
/// PostgreSQL contract runners.
/// </summary>
internal sealed class OccupancyCommitEfHarness
{
    public const string CatalogInstanceId = "catalog-ef";

    public static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly ProtobufIntegrationMessageSerializer Serializer = new();

    public OccupancyCommitEfHarness(ServiceOrderDbContext context, Guid owner, IOutboxWriter? outbox = null, FakeTimeProvider? clock = null)
    {
        Context = context;
        Owner = owner;
        Clock = clock ?? new FakeTimeProvider(Now);
        OrderTypes = new OrderTypeRegistry();
        Rules = new ServiceOrderRules(OrderTypes, timeProvider: Clock);
        Orders = new EFServiceOrderRepository(context, orderTypeRegistry: OrderTypes, timeProvider: Clock);
        Targets = new EFServiceOrderTargetRepository(context, OrderTypes, Clock);
        Occupancy = new EFPoleOccupancyRepository(NeutralOperatorTestContext.Over(context), Clock);
        ChangeSets = new EFOccupancyChangeSetRepository(NeutralOperatorTestContext.Over(context), Clock);
        UnitOfWork = new EFWorkflowUnitOfWork(context);
        var outboxWriter = outbox ?? new EFOutboxStore(context, Clock, Serializer);
        SagaStore = new EFOccupancyApplySagaStore(NeutralOperatorTestContext.Over(context), Occupancy);
        Service = new OccupancyCommitService(
            Orders, OrderTypes, Rules, Targets, Occupancy, Occupancy, ChangeSets, ChangeSets,
            outboxWriter, SagaStore, UnitOfWork,
            new OrderOccupancyOptions { CatalogInstanceId = CatalogInstanceId }, Clock);
        SagaService = new OccupancyApplySagaService(
            Orders, OrderTypes, Rules, SagaStore, outboxWriter, new EFIdempotentInbox(context, Clock), UnitOfWork,
            new TenantOrganizationScope(), Clock);
    }

    public ServiceOrderDbContext Context { get; }
    public Guid Owner { get; }
    public FakeTimeProvider Clock { get; }
    public OrderTypeRegistry OrderTypes { get; }
    public ServiceOrderRules Rules { get; }
    public EFServiceOrderRepository Orders { get; }
    public EFServiceOrderTargetRepository Targets { get; }
    public EFPoleOccupancyRepository Occupancy { get; }
    public EFOccupancyChangeSetRepository ChangeSets { get; }
    public EFWorkflowUnitOfWork UnitOfWork { get; }
    public OccupancyCommitService Service { get; }
    public EFOccupancyApplySagaStore SagaStore { get; }
    public OccupancyApplySagaService SagaService { get; }

    public OccupancyCommitContractHarness ToContractHarness(IAsyncDisposable? scope = null) => new(
        Service, Orders, Targets, Targets, Occupancy, Occupancy, ChangeSets, ChangeSets, OrderTypes, Clock, Owner, CatalogInstanceId,
        StagedBatchesAsync, SagaService, SagaStore, scope);

    /// <summary>The outbox rows as the contract suite reads asset mutation batches, oldest first.</summary>
    public async Task<IReadOnlyList<StagedAssetMutationBatch>> StagedBatchesAsync(CancellationToken ct)
    {
        var rows = await Context.OutboxMessages.AsNoTracking()
            .Where(m => m.MessageType == IntegrationEventTypes.AssetMutationBatchRequestedV1)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.PartitionKey)
            .ToListAsync(ct);
        return [.. rows.Select(row => new StagedAssetMutationBatch(
            row.MessageType, row.PartitionKey, row.CorrelationId, row.CausationId,
            Serializer.Deserialize<AssetMutationBatchRequestedEvent>(row.Payload)))];
    }
}
