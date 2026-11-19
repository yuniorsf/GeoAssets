using GeoAssets.Core.Messaging;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// One database session's worth of the global target ledger on real PostgreSQL (XD01-379): the EF store, the unit of
/// work, the EF outbox and inbox and a <see cref="TargetLedgerService"/> over them, for one organization. Two harnesses on
/// one database are two sessions, as two verticals' requests are.
/// </summary>
internal sealed class LedgerEfHarness : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private LedgerEfHarness(string connectionString, Guid organization, IReadOnlyCollection<GeoFeature> catalog, IOrderTypeDescriptorRegistry descriptors, OrderTypeRegistry orderTypes, Func<IOutboxWriter, IOutboxWriter>? decorateOutbox)
    {
        ConnectionString = connectionString;
        Organization = organization;
        Clock = new FakeTimeProvider(Now);
        Db = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(organization));
        Store = new EFTargetLedgerStore(Db, orderTypes);
        UnitOfWork = new EFWorkflowUnitOfWork(Db);
        Outbox = new EFOutboxStore(Db, Clock, new ProtobufIntegrationMessageSerializer(), "ledger-tests");
        Inbox = new EFIdempotentInbox(Db, Clock);
        Scope = new TenantOrganizationScope();
        Service = TargetLedgerServiceHarness.Create(
            Store, UnitOfWork, decorateOutbox?.Invoke(Outbox) ?? Outbox, Inbox, descriptors, catalog, Clock, Scope);
    }

    public string ConnectionString { get; }

    public Guid Organization { get; }

    public FakeTimeProvider Clock { get; }

    public ServiceOrderDbContext Db { get; }

    public EFTargetLedgerStore Store { get; }

    public EFWorkflowUnitOfWork UnitOfWork { get; }

    public EFOutboxStore Outbox { get; }

    public EFIdempotentInbox Inbox { get; }

    public TenantOrganizationScope Scope { get; }

    public TargetLedgerService Service { get; }

    /// <summary>A session on database <paramref name="database"/> for <paramref name="organization"/>; the database is emptied first when <paramref name="clear"/>.</summary>
    public static async Task<LedgerEfHarness> CreateAsync(
        string database,
        Guid organization,
        IReadOnlyCollection<GeoFeature> catalog,
        IOrderTypeDescriptorRegistry descriptors,
        bool clear = true,
        OrderTypeRegistry? orderTypes = null,
        Func<IOutboxWriter, IOutboxWriter>? decorateOutbox = null)
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(database);
        if (clear) await ClearAsync(connectionString);

        return new LedgerEfHarness(connectionString, organization, catalog, descriptors, orderTypes ?? new OrderTypeRegistry(), decorateOutbox);
    }

    /// <summary>Empties the ledger, the outbox, the inbox and every order of the database.</summary>
    public static async Task ClearAsync(string connectionString)
    {
        await using var db = PostgresTargetStoreDatabase.NewContext(connectionString);
        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "ServiceOrderTargets", "WorkflowOutboxMessages", "WorkflowInboxProcessedMessages", "ServiceOrders" CASCADE;""");
    }

    /// <summary>A descriptor of <paramref name="orderTypeId"/> with a target policy of the given exclusivity.</summary>
    public static OrderTypeDescriptor Descriptor(string verticalId, string orderTypeId, OrderTargetExclusivity exclusivity, params string[] terminalStatuses) => new()
    {
        VerticalId = verticalId,
        OrderTypeId = orderTypeId,
        DisplayNameKey = $"orderTypes.{orderTypeId}",
        ApiBaseRoute = "/api/orders",
        TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = exclusivity },
        TerminalStatuses = terminalStatuses,
    };

    /// <summary>The ledger order <paramref name="orderId"/> of a published order type, with the policy the vertical published.</summary>
    public TargetLedgerOrder OrderOf(string verticalId, string orderTypeId, string orderId, Guid? organization = null)
    {
        var descriptor = Service.FindOrderType(verticalId, orderTypeId) ?? throw new InvalidOperationException("The order type was not published.");
        return new TargetLedgerOrder(verticalId, orderId, orderTypeId, organization ?? Organization, descriptor.TargetPolicy!);
    }

    /// <summary>Every staged outbox message, oldest first, as (message type, partition key, decoded event).</summary>
    public async Task<List<(string MessageType, string PartitionKey, object Event)>> StagedAsync()
    {
        await using var read = PostgresTargetStoreDatabase.NewContext(ConnectionString);
        var rows = await read.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.MessageType).ToListAsync();
        var serializer = new ProtobufIntegrationMessageSerializer();

        return [.. rows.Select(row => (row.MessageType, row.PartitionKey, Decode(serializer, row.MessageType, row.Payload)))];
    }

    private static object Decode(ProtobufIntegrationMessageSerializer serializer, string messageType, byte[] payload) => messageType switch
    {
        _ when string.Equals(messageType, Core.Messaging.Events.IntegrationEventTypes.TargetsReservedV1, StringComparison.Ordinal) => serializer.Deserialize<TargetsReservedEvent>(payload),
        _ when string.Equals(messageType, Core.Messaging.Events.IntegrationEventTypes.TargetsReleasedV1, StringComparison.Ordinal) => serializer.Deserialize<TargetsReleasedEvent>(payload),
        _ when string.Equals(messageType, Core.Messaging.Events.IntegrationEventTypes.TargetsCommittedV1, StringComparison.Ordinal) => serializer.Deserialize<TargetsCommittedEvent>(payload),
        _ => throw new InvalidOperationException($"Unexpected message type {messageType}."),
    };

    /// <summary>Every stored target row of the database, read on a fresh session without any filter.</summary>
    public async Task<List<(string Vertical, string Order, string Feature, DateTime? ReleasedAt, string Status)>> RowsAsync()
    {
        await using var read = PostgresTargetStoreDatabase.NewContext(ConnectionString);
        var rows = await read.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(t => t.VerticalId).ThenBy(t => t.ServiceOrderId).ThenBy(t => t.FeatureId).ToListAsync();

        return [.. rows.Select(r => (r.VerticalId, r.ServiceOrderId, r.FeatureId, r.ReleasedAt, r.Status))];
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
