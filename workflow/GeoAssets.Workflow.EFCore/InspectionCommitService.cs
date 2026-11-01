using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Atomically commits a field order's supervisor sign-off (XD01-228): advances
/// <see cref="ServiceOrder.Status"/> to <see cref="FieldWorkStatus.Inspected"/> under optimistic
/// concurrency, posts one <see cref="ContractorUsPosting"/> ledger row per BoQ line (see
/// <see cref="ServiceOrderBoqAttributes"/> for how the lines/contractor are read off the order),
/// and stages an <see cref="AssetMutationBatchRequestedEvent"/> on the transactional outbox for
/// async Core GIS mutation — all three writes commit or roll back together.
///
/// Atomicity depends on <see cref="db"/>/<paramref name="serviceOrders"/>-via-DI/
/// <paramref name="postings"/>-via-DI/<paramref name="outboxWriter"/>-via-DI all resolving to
/// repositories backed by the exact same <see cref="ServiceOrderDbContext"/> instance (i.e. all
/// registered <c>Scoped</c> in the same DI scope/HTTP request) — the explicit transaction below
/// is opened on <see cref="db"/>'s connection, and every repository call here must share it or
/// the "single transaction" guarantee silently breaks.
///
/// Deliberately excludes this ticket's own blueprint text's "inserting
/// field_icerp_paving_measurements when applicable, spawning a child paving sub-order" —
/// no persisted schema for that table exists yet (XD01-230 only built the in-memory
/// domain/validation logic). Tracked separately; this service only implements the three writes
/// XD01-228's own Acceptance Criteria actually test.
/// </summary>
public sealed class InspectionCommitService(
    ServiceOrderDbContext db,
    IServiceOrderRepository serviceOrders,
    IContractorUsPostingRepository postings,
    IUsCatalogRepository catalog,
    IOutboxWriter outboxWriter,
    OrderTypeRegistry orderTypeRegistry,
    TimeProvider timeProvider)
{
    public async Task<InspectionCommitResult> CommitAsync(
        string serviceOrderId,
        byte[] expectedRowVersion,
        string performedByUserId,
        string catalogInstanceId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentNullException.ThrowIfNull(expectedRowVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(performedByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogInstanceId);

        var order = await serviceOrders.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{serviceOrderId}' not found.");

        if (order is not ServiceOrder concreteOrder)
        {
            throw new InvalidOperationException($"Expected a {nameof(ServiceOrder)} instance, got {order.GetType().Name}.");
        }

        var fieldWorkOrderType = orderTypeRegistry.Find(order.OrderTypeId)
            ?? throw new KeyNotFoundException($"OrderType '{order.OrderTypeId}' is not registered.");

        var contractorOrganizationId = ServiceOrderBoqAttributes.ReadContractorOrganizationId(order.Attributes);
        var lines = ServiceOrderBoqAttributes.ReadBoqLines(order.Attributes);

        var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                var postedPostings = await PostLedgerLinesAsync(order, contractorOrganizationId, lines, ct).ConfigureAwait(false);

                concreteOrder.Transition(FieldWorkStatus.Inspected, timeProvider, fieldWorkOrderType);
                await serviceOrders.UpdateAsync(CopyWithRowVersion(concreteOrder, expectedRowVersion), ct).ConfigureAwait(false);

                var outboxMessageId = await StageMutationEventAsync(order, performedByUserId, catalogInstanceId, ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return new InspectionCommitResult
                {
                    ServiceOrderId = order.Id,
                    TotalUs = postedPostings.Sum(p => p.TotalUs),
                    TotalBrl = postedPostings.Sum(p => p.TotalBrl),
                    Postings = postedPostings,
                    OutboxMessageId = outboxMessageId,
                };
            }
            catch
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task<List<ContractorUsPosting>> PostLedgerLinesAsync(
        IServiceOrder order,
        Guid contractorOrganizationId,
        IReadOnlyList<(string ItemCode, decimal Quantity)> lines,
        CancellationToken ct)
    {
        var postedPostings = new List<ContractorUsPosting>(lines.Count);
        foreach (var (itemCode, quantity) in lines)
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var catalogItem = await catalog.FindEffectiveAsync(order.OrganizationId, itemCode, nowUtc, ct).ConfigureAwait(false)
                ?? throw new UsCatalogItemNotFoundException(order.OrganizationId, itemCode, nowUtc);

            var posting = ContractorUsPostingFactory.Post(order.Id, contractorOrganizationId, catalogItem, quantity, timeProvider);
            await postings.AddAsync(posting, ct).ConfigureAwait(false);
            postedPostings.Add(posting);
        }

        if (postedPostings.Count > 0)
        {
            await postings.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return postedPostings;
    }

    private Task<Guid> StageMutationEventAsync(
        IServiceOrder order, string performedByUserId, string catalogInstanceId, CancellationToken ct)
    {
        var mutationEvent = AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: catalogInstanceId,
            organizationId: order.OrganizationId,
            requestedByService: "GeoAssets.FieldService.Server",
            performedByUserId: performedByUserId,
            mutations: [],
            timeProvider: timeProvider,
            sourceOrderId: order.Id,
            sourceOrderTypeId: order.OrderTypeId);

        return outboxWriter.EnqueueAsync(
            mutationEvent,
            messageType: IntegrationEventTypes.AssetMutationBatchRequestedV1,
            partitionKey: order.Id,
            ct: ct);
    }

    private static ServiceOrder CopyWithRowVersion(ServiceOrder source, byte[] rowVersion) => new()
    {
        Id = source.Id,
        RowVersion = rowVersion,
        Title = source.Title,
        Description = source.Description,
        OrderTypeId = source.OrderTypeId,
        Status = source.Status,
        Priority = source.Priority,
        CreatedBy = source.CreatedBy,
        AssignedTo = source.AssignedTo,
        OrganizationId = source.OrganizationId,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        ScheduledAt = source.ScheduledAt,
        CompletedAt = source.CompletedAt,
        Attributes = source.Attributes,
        Features = source.Features,
        SelectionSpec = source.SelectionSpec,
        FeatureIds = source.FeatureIds,
        ParentOrderId = source.ParentOrderId,
    };
}
