using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.FieldService.Persistence;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// XD01-381 on a real PostgreSQL: the FieldService context and the kernel context are two EF contexts on one
/// <see cref="System.Data.Common.DbConnection"/>, and what the FieldService stores write joins the transaction the inspection
/// commit opened on the kernel context — so its postings, the order transition, the paving child order, the paving measurement and
/// the outbox message are one atomic write. The schema is the kernel's (<c>EnsureCreated</c>) plus the FieldService chain's
/// migrations, foreign keys to the kernel's orders included (<see cref="PostgresTargetStoreDatabase"/>).
/// </summary>
public sealed class FieldServiceSharedTransactionTests : IAsyncDisposable
{
    private const string DatabaseName = "field_service_shared_transaction";
    private const string CatalogInstance = "catalog-primary";

    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProtobufIntegrationMessageSerializer Serializer = new();
    private static readonly OrderType WaterSewerRepairOrderType = FieldWorkWorkflowGraph.BuildOrderType("water-sewer-repair", "Water/Sewer Repair");
    private static readonly OrderType PavingRestorationOrderType = FieldWorkWorkflowGraph.BuildOrderType("water-paving-restoration", "Water Paving Restoration");

    private readonly DisposableGroup _scope = new();
    private readonly OrderTypeRegistry _registry = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    private sealed class Stack
    {
        public required string ConnectionString { get; init; }

        public required ServiceOrderDbContext Kernel { get; init; }

        public required FieldServiceDbContext FieldService { get; init; }

        public required EFServiceOrderRepository Orders { get; init; }

        public required EFContractorUsPostingRepository Postings { get; init; }

        public required EFUsCatalogRepository Catalog { get; init; }

        public required EFPavingMeasurementRepository Paving { get; init; }

        public required EFOutboxStore Outbox { get; init; }
    }

    private async Task<Stack> NewStackAsync()
    {
        _registry.Register(WaterSewerRepairOrderType);
        _registry.Register(PavingRestorationOrderType);
        FieldWorkOrderTypes.RegisterAll(_registry);

        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await using (var reset = PostgresTargetStoreDatabase.NewContext(connectionString))
        {
            await reset.Database.ExecuteSqlRawAsync(
                """TRUNCATE TABLE "ServiceOrders", "WorkflowOutboxMessages", field_service."UsCatalogItems", field_service."ContractorUsLedgerEntries" CASCADE;""");
        }

        var kernel = _scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner)));
        var fieldService = _scope.Add(FieldServiceTestContext.Over(kernel));
        return new Stack
        {
            ConnectionString = connectionString,
            Kernel = kernel,
            FieldService = fieldService,
            Orders = new EFServiceOrderRepository(kernel, orderTypeRegistry: _registry, timeProvider: _clock),
            Postings = new EFContractorUsPostingRepository(fieldService),
            Catalog = new EFUsCatalogRepository(fieldService),
            Paving = new EFPavingMeasurementRepository(fieldService),
            Outbox = new EFOutboxStore(kernel, _clock, Serializer, "test-service"),
        };
    }

    private InspectionCommitService BuildService(Stack stack, IOutboxWriter? outbox = null) => new(
        stack.Kernel,
        new ValidatingServiceOrderRepository(stack.Orders, _registry),
        stack.Postings,
        stack.Catalog,
        stack.Paving,
        outbox ?? stack.Outbox,
        _registry,
        _clock);

    private static async Task SeedCatalogItemAsync(Stack stack, string itemCode)
    {
        await stack.Catalog.AddAsync(new UsCatalogItem
        {
            OrganizationId = Owner,
            ItemCode = itemCode,
            BaseUsFactor = 2.5m,
            RegionalMultiplier = 1.2m,
            MonetaryConversionRate = 85m,
            EffectiveFrom = Now.UtcDateTime.AddDays(-1),
        });
        await stack.Catalog.SaveChangesAsync();
    }

    private static GeoPolygon SquareOfSide(double originLon, double originLat, double sideMeters)
    {
        const double metersPerDegreeLatitude = 111_320.0;
        var deltaLon = sideMeters / (metersPerDegreeLatitude * Math.Cos(originLat * Math.PI / 180.0));
        var deltaLat = sideMeters / metersPerDegreeLatitude;
        return new GeoPolygon([
            (originLon, originLat),
            (originLon + deltaLon, originLat),
            (originLon + deltaLon, originLat + deltaLat),
            (originLon, originLat + deltaLat),
            (originLon, originLat),
        ]);
    }

    /// <summary>A completed, paving-eligible water/sewer repair whose recorded trench agrees with its boundary (10 m x 10 m), with the BoQ lines given.</summary>
    private static async Task<(string OrderId, byte[] RowVersion)> SeedPavingEligibleOrderAsync(Stack stack, string boqLinesJson)
    {
        var boundaryGeoJson = JsonSerializer.Serialize(SquareOfSide(-46.633, -23.55, sideMeters: 10), GeoJsonSerializer.Options);
        var order = new ServiceOrder
        {
            OrderTypeId = WaterSewerRepairOrderType.Id,
            Status = FieldWorkStatus.Completed,
            CreatedBy = "tech-1",
            OrganizationId = Owner,
            CreatedAt = Now.UtcDateTime,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ServiceOrderBoqAttributes.ContractorOrganizationIdKey] = Owner.ToString(),
                [ServiceOrderBoqAttributes.BoqLinesKey] = boqLinesJson,
                [ServiceOrderPavingAttributes.PavingEligibleKey] = "true",
                [ServiceOrderPavingAttributes.PavingOrderTypeIdKey] = PavingRestorationOrderType.Id,
                [ServiceOrderPavingAttributes.RecordedTrenchLengthMetersKey] = "10",
                [ServiceOrderPavingAttributes.RecordedTrenchWidthMetersKey] = "10",
                [ServiceOrderPavingAttributes.RecordedTrenchDepthMetersKey] = "1.5",
                [ServiceOrderPavingAttributes.RecordedBoundaryGeoJsonKey] = boundaryGeoJson,
            },
        };

        await stack.Orders.AddAsync(order);
        var loaded = await stack.Orders.GetByIdAsync(order.Id) ?? throw new InvalidOperationException("Seed order not found.");
        return (loaded.Id, loaded.RowVersion);
    }

    private sealed record Stored(int Postings, int Measurements, int Orders, string ParentStatus, int OutboxMessages);

    /// <summary>What the database holds, read through contexts nothing else has touched.</summary>
    private async Task<Stored> StoredAsync(Stack stack, string parentOrderId)
    {
        var verify = _scope.Add(PostgresTargetStoreDatabase.NewContext(stack.ConnectionString, new FakeTenantContext(Owner)));
        var verifyFieldService = _scope.Add(FieldServiceTestContext.Over(verify));
        return new Stored(
            await verifyFieldService.ContractorUsPostings.AsNoTracking().CountAsync(),
            await verifyFieldService.PavingMeasurements.AsNoTracking().CountAsync(),
            await verify.ServiceOrders.AsNoTracking().CountAsync(),
            await verify.ServiceOrders.AsNoTracking().Where(o => o.Id == parentOrderId).Select(o => o.Status).SingleAsync(),
            await verify.OutboxMessages.AsNoTracking().CountAsync());
    }

    /// <summary>An outbox that really writes the message — inside the commit's transaction — and then fails: the last write of the commit.</summary>
    private sealed class WritesThenFailsOutbox(IOutboxWriter inner) : IOutboxWriter
    {
        public async Task<Guid> EnqueueAsync<TEvent>(
            TEvent message, string messageType, string partitionKey, string? catalogInstanceId = null, string? correlationId = null,
            string? causationId = null, CancellationToken ct = default)
            where TEvent : notnull
        {
            await inner.EnqueueAsync(message, messageType, partitionKey, catalogInstanceId, correlationId, causationId, ct);
            throw new InvalidOperationException("boom");
        }

        public Task EnqueueEnvelopeAsync(IntegrationEnvelope envelope, CancellationToken ct = default) => inner.EnqueueEnvelopeAsync(envelope, ct);
    }

    [Fact]
    public async Task CommitAsync_PavingEligible_Succeeds_CommitsPostingsOrderTransitionChildOrderMeasurementAndOutboxTogether()
    {
        var stack = await NewStackAsync();
        await SeedCatalogItemAsync(stack, "US-EL-014");
        var (orderId, rowVersion) = await SeedPavingEligibleOrderAsync(stack, """[{"itemCode":"US-EL-014","quantity":2}]""");

        var result = await BuildService(stack).CommitAsync(orderId, rowVersion, "inspector-1", CatalogInstance);

        result.PavingChildServiceOrderId.Should().NotBeNullOrEmpty();
        (await StoredAsync(stack, orderId)).Should().Be(new Stored(Postings: 1, Measurements: 1, Orders: 2, FieldWorkStatus.Inspected, OutboxMessages: 1));
        stack.Kernel.Database.CurrentTransaction.Should().BeNull("the commit's transaction ended");
        (await stack.Postings.GetByServiceOrderIdAsync(orderId)).Should().ContainSingle("the vertical's store works on after the commit");
        stack.FieldService.Database.CurrentTransaction.Should().BeNull("the vertical's context let the ended transaction go on its next use");
        await using var verify = PostgresTargetStoreDatabase.NewContext(stack.ConnectionString, new FakeTenantContext(Owner));
        (await verify.ServiceOrders.AsNoTracking().SingleAsync(o => o.Id == result.PavingChildServiceOrderId)).ParentOrderId.Should().Be(orderId);
        await using var verifyFieldService = FieldServiceTestContext.Over(verify);
        var measurement = await verifyFieldService.PavingMeasurements.AsNoTracking().SingleAsync();
        measurement.ParentServiceOrderId.Should().Be(orderId);
        measurement.ChildServiceOrderId.Should().Be(result.PavingChildServiceOrderId);
    }

    [Fact]
    public async Task CommitAsync_WhenTheLastWriteFailsAfterThePavingWrite_RollsBackPostingsChildOrderMeasurementOutboxAndTheOrderTransition()
    {
        var stack = await NewStackAsync();
        await SeedCatalogItemAsync(stack, "US-EL-014");
        var (orderId, rowVersion) = await SeedPavingEligibleOrderAsync(stack, """[{"itemCode":"US-EL-014","quantity":2}]""");
        var service = BuildService(stack, new WritesThenFailsOutbox(stack.Outbox));

        var act = () => service.CommitAsync(orderId, rowVersion, "inspector-1", CatalogInstance);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        stack.Kernel.Database.CurrentTransaction.Should().BeNull();
        (await StoredAsync(stack, orderId)).Should().Be(
            new Stored(Postings: 0, Measurements: 0, Orders: 1, FieldWorkStatus.Completed, OutboxMessages: 0),
            "the posting, the child order and the measurement were flushed, and the outbox row written, before the failure — and every one of them rolled back");
        (await stack.Postings.GetByServiceOrderIdAsync(orderId)).Should().BeEmpty("the vertical's context works on after the rollback, with no stale transaction");
    }

    [Fact]
    public async Task CommitAsync_WithAStaleRowVersion_FailsAfterThePostingsAndThePavingWrites_AndRollsEverythingBack()
    {
        var stack = await NewStackAsync();
        await SeedCatalogItemAsync(stack, "US-EL-014");
        var (orderId, _) = await SeedPavingEligibleOrderAsync(stack, """[{"itemCode":"US-EL-014","quantity":2}]""");
        var service = BuildService(stack);

        // The parent's update — the one that carries the optimistic-concurrency check — comes after postings, child and measurement.
        var act = () => service.CommitAsync(orderId, [1, 2, 3, 4, 5, 6, 7, 8], "inspector-1", CatalogInstance);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        (await StoredAsync(stack, orderId)).Should().Be(new Stored(Postings: 0, Measurements: 0, Orders: 1, FieldWorkStatus.Completed, OutboxMessages: 0));
    }

    [Fact]
    public async Task CommitAsync_WhenALaterBoqLineHasNoCatalogItem_RollsBackTheEarlierLinesPostingAndTheOrder()
    {
        var stack = await NewStackAsync();
        await SeedCatalogItemAsync(stack, "US-EL-014");
        var (orderId, rowVersion) = await SeedPavingEligibleOrderAsync(
            stack, """[{"itemCode":"US-EL-014","quantity":2},{"itemCode":"US-MISSING","quantity":1}]""");

        var act = () => BuildService(stack).CommitAsync(orderId, rowVersion, "inspector-1", CatalogInstance);

        await act.Should().ThrowAsync<UsCatalogItemNotFoundException>();
        (await StoredAsync(stack, orderId)).Should().Be(new Stored(Postings: 0, Measurements: 0, Orders: 1, FieldWorkStatus.Completed, OutboxMessages: 0),
            "the first line's posting was flushed before the second failed");
    }

    [Fact]
    public async Task PostingsWrite_InsideACallersTransactionOnTheKernelContext_IsRolledBackWithIt_AndCommittedWithIt()
    {
        var stack = await NewStackAsync();
        var (orderId, _) = await SeedPavingEligibleOrderAsync(stack, "[]");
        var posting = new ContractorUsPosting
        {
            ServiceOrderId = orderId,
            ContractorOrganizationId = Owner,
            ItemCode = "US-EL-014",
            Quantity = 1,
            UnitUs = 1,
            MonetaryRate = 85m,
            PostedAtUtc = Now.UtcDateTime,
        };

        await using (var rolledBack = await stack.Kernel.Database.BeginTransactionAsync())
        {
            await stack.Postings.AddAsync(posting);
            await stack.Postings.SaveChangesAsync();
            stack.FieldService.Database.CurrentTransaction!.GetDbTransaction().Should().BeSameAs(rolledBack.GetDbTransaction(), "the vertical's context joined it");
            await rolledBack.RollbackAsync();
        }

        (await StoredAsync(stack, orderId)).Postings.Should().Be(0);

        await using (var committed = await stack.Kernel.Database.BeginTransactionAsync())
        {
            await stack.Postings.AddAsync(new ContractorUsPosting
            {
                ServiceOrderId = orderId,
                ContractorOrganizationId = Owner,
                ItemCode = "US-EL-014",
                Quantity = 1,
                UnitUs = 1,
                MonetaryRate = 85m,
                PostedAtUtc = Now.UtcDateTime,
            });
            await stack.Postings.SaveChangesAsync();
            await committed.CommitAsync();
        }

        (await StoredAsync(stack, orderId)).Postings.Should().Be(1);
    }

    [Fact]
    public async Task OneDiScope_GivesTheFieldServiceContextAndTheKernelContextTheSameDbConnectionInstance()
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(Owner));
        services.AddDbContext<ServiceOrderDbContext>(options => options.UseNpgsql(connectionString));
        services.AddFieldServicePersistence();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        await using var otherScope = provider.CreateAsyncScope();

        var kernel = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
        var fieldService = scope.ServiceProvider.GetRequiredService<FieldServiceDbContext>();
        var otherKernel = otherScope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();

        fieldService.Database.GetDbConnection().Should().BeSameAs(kernel.Database.GetDbConnection());
        otherKernel.Database.GetDbConnection().Should().NotBeSameAs(kernel.Database.GetDbConnection(), "every scope has its own connection");
        scope.ServiceProvider.GetRequiredService<IAmbientDbTransaction>().Current.Should().BeNull();

        await using var transaction = await kernel.Database.BeginTransactionAsync();
        scope.ServiceProvider.GetRequiredService<IAmbientDbTransaction>().Current.Should().BeSameAs(transaction.GetDbTransaction());
        var ledger = scope.ServiceProvider.GetRequiredService<IContractorUsLedgerRepository>();
        var entry = new ContractorUsLedgerEntry
        {
            OrganizationId = Owner,
            UsCode = "US-DI",
            Description = "di",
            Quantity = 1,
            UnitValue = 1,
            PhotoProof = [],
            CreatedAt = Now.UtcDateTime,
        };
        await ledger.AddAsync(entry);
        await ledger.SaveChangesAsync();
        fieldService.Database.CurrentTransaction!.GetDbTransaction().Should().BeSameAs(transaction.GetDbTransaction(), "the vertical's context joined the kernel's transaction");
        await transaction.RollbackAsync();

        (await ledger.GetByIdAsync(entry.Id)).Should().BeNull();
    }
}
