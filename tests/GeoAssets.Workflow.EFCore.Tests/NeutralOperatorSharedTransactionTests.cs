using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence;
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
/// XD01-380 on a real PostgreSQL: the NeutralOperator context and the kernel context are two EF contexts on one
/// <see cref="System.Data.Common.DbConnection"/>, and what the occupancy stores write joins the transaction the kernel's
/// unit of work, order repository or caller has open — so an approval's occupancy write, order transition and outbox
/// message are one atomic write, and closing an order discards its staged change sets in the closing transaction.
/// </summary>
public sealed class NeutralOperatorSharedTransactionTests : IAsyncDisposable
{
    private const string DatabaseName = "neutral_operator_shared_transaction";
    private const string CloseFailureDatabaseName = "neutral_operator_close_failure";

    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;
    private static readonly DateTimeOffset Created = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClosedAt = new(2026, 11, 19, 17, 30, 0, TimeSpan.Zero);
    private static readonly ProtobufIntegrationMessageSerializer Serializer = new();

    private readonly DisposableGroup _scope = new();
    private readonly OrderTypeRegistry _orderTypes = new();
    private readonly FakeTimeProvider _clock = new(Created);

    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    private sealed class Stack
    {
        public required string ConnectionString { get; init; }

        public required ServiceOrderDbContext Kernel { get; init; }

        public required NeutralOperatorDbContext NeutralOperator { get; init; }

        public required EFServiceOrderRepository Orders { get; init; }

        public required EFServiceOrderTargetRepository Targets { get; init; }

        public required EFPoleOccupancyRepository Occupancy { get; init; }

        public required EFOccupancyChangeSetRepository ChangeSets { get; init; }

        public required EFOutboxStore Outbox { get; init; }

        public required EFWorkflowUnitOfWork UnitOfWork { get; init; }
    }

    private async Task<Stack> NewStackAsync(string databaseName = DatabaseName, bool withReleaseHook = false)
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(databaseName);
        await using (var reset = PostgresTargetStoreDatabase.NewContext(connectionString))
        {
            await reset.Database.ExecuteSqlRawAsync(
                """TRUNCATE TABLE "ServiceOrders", "WorkflowOutboxMessages", neutral_operator.field_pole_occupancies CASCADE;""");
        }

        var kernel = _scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner)));
        var neutralOperator = _scope.Add(NeutralOperatorTestContext.Over(kernel));
        var changeSets = new EFOccupancyChangeSetRepository(neutralOperator, _clock);
        return new Stack
        {
            ConnectionString = connectionString,
            Kernel = kernel,
            NeutralOperator = neutralOperator,
            Orders = new EFServiceOrderRepository(
                kernel, orderTypeRegistry: _orderTypes, timeProvider: _clock,
                hooks: withReleaseHook ? [new OccupancyReleaseLifecycleHook(changeSets)] : null),
            Targets = new EFServiceOrderTargetRepository(kernel, _orderTypes, _clock),
            Occupancy = new EFPoleOccupancyRepository(neutralOperator, _clock),
            ChangeSets = changeSets,
            Outbox = new EFOutboxStore(kernel, _clock, Serializer),
            UnitOfWork = new EFWorkflowUnitOfWork(kernel),
        };
    }

    private static async Task SeedOrderAsync(Stack stack, string orderId, params string[] poles)
    {
        await stack.Orders.AddAsync(new ServiceOrder
        {
            Id = orderId, Title = orderId, OrderTypeId = "inspection", OrganizationId = Owner, CreatedBy = "u1", CreatedAt = Created.UtcDateTime,
        });
        await stack.Targets.AddRangeAsync(
            orderId,
            [.. poles.Select(pole => new ServiceOrderTargetBuilder().WithId($"{orderId}:{pole}").WithOrderId(orderId).WithFeatureId(pole).WithOrganizationId(Owner).Build())],
            CancellationToken.None);
    }

    private static PoleOccupancyEntry Entry(string pole) =>
        new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(Owner).OnSlot(1).Build();

    private static OccupancyChangeSet NewSet(string orderId, string pole, DateTimeOffset at) =>
        OccupancyChangeSet.Stage(orderId, $"{orderId}:{pole}", pole, Owner, [OccupancyChangeBuilder.Add(2)], null, "crew-1", at);

    private static FeatureCatalogMutatedEvent SampleEvent() =>
        new(
            CatalogInstanceId: "catalog-main",
            OrganizationId: Owner,
            ScopeId: "sector-1",
            FeatureId: "pole-1",
            MutationKind: AssetMutationKind.UpsertFeature,
            Name: "Pole 1",
            AssetTypeId: "pole",
            LayerId: "layer-poles",
            Geometry: new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [-38.5014, -12.9714], [], []),
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal),
            AffectedBounds: new Rfc7946BoundingBox(-38.51, -12.98, -38.50, -12.97),
            RowVersion: [0x01, 0x02, 0x03, 0x04],
            MutatedAtUtc: Created,
            CorrelationId: "corr-no");

    /// <summary>An occupancy write, an order transition and an outbox message — each through its own store.</summary>
    private static async Task<int> WriteEverythingAsync(Stack stack, bool failAfterTheOccupancyWrite, CancellationToken ct)
    {
        await stack.Occupancy.ReplaceEntriesAsync("pole-1", PoleOccupancy.UnsavedVersion, [Entry("pole-1")], ct);
        stack.Kernel.Database.CurrentTransaction.Should().NotBeNull("the unit of work opened one on the kernel connection");
        (await stack.Occupancy.GetForPoleAsync("pole-1", ct))!.Entries.Should().ContainSingle("the occupancy store sees its own write in that transaction");
        if (failAfterTheOccupancyWrite)
            throw new InvalidOperationException("boom");

        var order = (ServiceOrder)(await stack.Orders.GetByIdAsync("ord-1", ct))!;
        order.Status = ServiceOrderStatus.Pending;
        await stack.Orders.UpdateAsync(order, ct);
        await stack.Outbox.EnqueueAsync(SampleEvent(), IntegrationEventTypes.FeatureCatalogMutatedV1, "catalog-main:pole-1", "catalog-main", "corr-no", ct: ct);
        return 1;
    }

    private async Task<(int Poles, int Entries, string OrderStatus, int OutboxMessages)> StoredAsync(string connectionString)
    {
        var verify = _scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner)));
        var verifyNeutralOperator = _scope.Add(NeutralOperatorTestContext.Over(verify));
        return (
            await verifyNeutralOperator.PoleOccupancies.AsNoTracking().CountAsync(),
            await verifyNeutralOperator.PoleOccupancyEntries.AsNoTracking().CountAsync(),
            await verify.ServiceOrders.AsNoTracking().Select(o => o.Status).SingleAsync(),
            await verify.OutboxMessages.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task UnitOfWork_WhenTheWorkThrowsAfterTheOccupancyWrite_RollsBackTheOccupancyTheOrderTransitionAndTheOutbox()
    {
        var stack = await NewStackAsync();
        await SeedOrderAsync(stack, "ord-1", "pole-1");

        var act = () => stack.UnitOfWork.ExecuteAsync(ct => WriteEverythingAsync(stack, failAfterTheOccupancyWrite: true, ct), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        stack.Kernel.Database.CurrentTransaction.Should().BeNull();
        (await StoredAsync(stack.ConnectionString)).Should().Be((0, 0, ServiceOrderStatus.Draft, 0));
        (await stack.Occupancy.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeNull("the next use of the occupancy store starts clean");
    }

    [Fact]
    public async Task UnitOfWork_WhenTheWorkSucceeds_CommitsTheOccupancyTheOrderTransitionAndTheOutboxTogether()
    {
        var stack = await NewStackAsync();
        await SeedOrderAsync(stack, "ord-1", "pole-1");

        await stack.UnitOfWork.ExecuteAsync(ct => WriteEverythingAsync(stack, failAfterTheOccupancyWrite: false, ct), CancellationToken.None);

        (await StoredAsync(stack.ConnectionString)).Should().Be((1, 1, ServiceOrderStatus.Pending, 1));
        stack.Kernel.Database.CurrentTransaction.Should().BeNull();
        (await stack.Occupancy.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().ContainSingle("the store works on after the unit of work ended");
    }

    [Fact]
    public async Task OccupancyWrite_InsideACallersTransactionOnTheKernelContext_IsRolledBackWithIt_AndCommittedWithIt()
    {
        var stack = await NewStackAsync();

        await using (var rolledBack = await stack.Kernel.Database.BeginTransactionAsync())
        {
            await stack.Occupancy.ReplaceEntriesAsync("pole-1", PoleOccupancy.UnsavedVersion, [Entry("pole-1")], CancellationToken.None);
            await rolledBack.RollbackAsync();
        }

        (await PoleCountAsync(stack)).Should().Be(0);

        await using (var committed = await stack.Kernel.Database.BeginTransactionAsync())
        {
            await stack.Occupancy.ReplaceEntriesAsync("pole-1", PoleOccupancy.UnsavedVersion, [Entry("pole-1")], CancellationToken.None);
            await committed.CommitAsync();
        }

        (await PoleCountAsync(stack)).Should().Be(1);
    }

    private static async Task<int> PoleCountAsync(Stack stack)
    {
        await using var verify = PostgresTargetStoreDatabase.NewContext(stack.ConnectionString, new FakeTenantContext(Owner));
        await using var verifyNeutralOperator = NeutralOperatorTestContext.Over(verify);
        return await verifyNeutralOperator.PoleOccupancies.AsNoTracking().CountAsync();
    }

    [Fact]
    public async Task OneDiScope_GivesTheNeutralOperatorContextAndTheKernelContextTheSameDbConnectionInstance()
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddScoped<ITenantContext>(_ => new FakeTenantContext(Owner));
        services.AddDbContext<ServiceOrderDbContext>(options => options.UseNpgsql(connectionString));
        services.AddNeutralOperatorPersistence();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        await using var otherScope = provider.CreateAsyncScope();

        var kernel = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();
        var neutralOperator = scope.ServiceProvider.GetRequiredService<NeutralOperatorDbContext>();
        var otherKernel = otherScope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();

        neutralOperator.Database.GetDbConnection().Should().BeSameAs(kernel.Database.GetDbConnection());
        otherKernel.Database.GetDbConnection().Should().NotBeSameAs(kernel.Database.GetDbConnection(), "every scope has its own connection");
        scope.ServiceProvider.GetRequiredService<IAmbientDbTransaction>().Current.Should().BeNull();

        await using var transaction = await kernel.Database.BeginTransactionAsync();
        scope.ServiceProvider.GetRequiredService<IAmbientDbTransaction>().Current.Should().BeSameAs(transaction.GetDbTransaction());
        await scope.ServiceProvider.GetRequiredService<IPoleOccupancyWriter>()
            .ReplaceEntriesAsync("pole-di", PoleOccupancy.UnsavedVersion, [Entry("pole-di")], CancellationToken.None);
        neutralOperator.Database.CurrentTransaction!.GetDbTransaction().Should().BeSameAs(transaction.GetDbTransaction(), "the vertical's context joined the kernel's transaction");
        await transaction.RollbackAsync();

        (await scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>().GetForPoleAsync("pole-di", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ClosingAnOrder_DiscardsStagedChangeSets_AndLeavesCommittedOnes()
    {
        var stack = await NewStackAsync(withReleaseHook: true);
        await SeedOrderAsync(stack, "ord-1", "pole-1", "pole-2");
        await stack.ChangeSets.StageAsync(NewSet("ord-1", "pole-1", Created), CancellationToken.None);
        var committed = await stack.ChangeSets.StageAsync(NewSet("ord-1", "pole-2", Created), CancellationToken.None);
        (await stack.ChangeSets.MarkCommittedAsync(committed.Commit(Created.AddHours(1)), CancellationToken.None)).Should().BeTrue();

        _clock.SetUtcNow(ClosedAt);
        var order = (ServiceOrder)(await stack.Orders.GetByIdAsync("ord-1"))!;
        order.Status = ServiceOrderStatus.Cancelled;
        await stack.Orders.UpdateAsync(order);

        await using var verify = PostgresTargetStoreDatabase.NewContext(stack.ConnectionString, new FakeTenantContext(Owner));
        await using var verifyNeutralOperator = NeutralOperatorTestContext.Over(verify);
        var sets = await verifyNeutralOperator.OccupancyChangeSets.AsNoTracking().OrderBy(s => s.PoleFeatureId).ToListAsync();
        sets.Select(s => (s.PoleFeatureId, s.Status)).Should().Equal(("pole-1", "Discarded"), ("pole-2", "Committed"));
        sets[0].ClosedAt.Should().Be(ClosedAt.UtcDateTime, "the discard carries the instant the order closed");
        sets[1].ClosedAt.Should().Be(Created.AddHours(1).UtcDateTime, "a committed set is not touched");
        (await verify.ServiceOrders.AsNoTracking().Select(o => o.Status).SingleAsync()).Should().Be(ServiceOrderStatus.Cancelled);
    }

    [Fact]
    public async Task ClosingAnOrder_WhoseTransitionFailsAfterTheHookRan_RollsTheDiscardBackWithIt()
    {
        var stack = await NewStackAsync(CloseFailureDatabaseName, withReleaseHook: true);
        await SeedOrderAsync(stack, "ord-1", "pole-1");
        await stack.ChangeSets.StageAsync(NewSet("ord-1", "pole-1", Created), CancellationToken.None);
        await stack.Kernel.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ServiceOrders" ADD CONSTRAINT "CK_no_cancelled_orders" CHECK ("Status" <> 'Cancelled')""");
        try
        {
            _clock.SetUtcNow(ClosedAt);
            var order = (ServiceOrder)(await stack.Orders.GetByIdAsync("ord-1"))!;
            order.Status = ServiceOrderStatus.Cancelled;

            var act = () => stack.Orders.UpdateAsync(order);

            await act.Should().ThrowAsync<DbUpdateException>("the order's own save is what fails, after the hook's discard was written");
            stack.Kernel.Database.CurrentTransaction.Should().BeNull();
            await using var verify = PostgresTargetStoreDatabase.NewContext(stack.ConnectionString, new FakeTenantContext(Owner));
            await using var verifyNeutralOperator = NeutralOperatorTestContext.Over(verify);
            var set = await verifyNeutralOperator.OccupancyChangeSets.AsNoTracking().SingleAsync();
            (set.Status, set.ClosedAt).Should().Be(("Staged", (DateTime?)null));
            (await verify.ServiceOrders.AsNoTracking().Select(o => o.Status).SingleAsync()).Should().Be(ServiceOrderStatus.Draft);
        }
        finally
        {
            await stack.Kernel.Database.ExecuteSqlRawAsync("""ALTER TABLE "ServiceOrders" DROP CONSTRAINT IF EXISTS "CK_no_cancelled_orders" """);
        }
    }

    [Fact]
    public async Task StageAsync_AgainstARemoteVerticalsLedgerRowOfTheSameOrderAndPole_IsRefusedLikeAnUnknownTarget_WhileTheKernelRowIsAccepted()
    {
        var stack = await NewStackAsync();
        await SeedOrderAsync(stack, "ord-1", "pole-1");
        await stack.Kernel.Database.ExecuteSqlAsync($"""
            INSERT INTO "ServiceOrderTargets"
                ("Id", "ServiceOrderId", "VerticalId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
            VALUES ('isp:ord-1:pole-1', 'ord-1', 'isp', 'pole-1', 'pole', '[]', 'Pending', 0, {Created.UtcDateTime}, 'tester', {Owner}, 0),
                   ('isp:remote-only:pole-1', 'remote-only', 'isp', 'pole-1', 'pole', '[]', 'Pending', 0, {Created.UtcDateTime}, 'tester', {Owner}, 0)
            """);

        var againstRemoteRowOfTheSameOrder = () => stack.ChangeSets.StageAsync(
            OccupancyChangeSet.Stage("ord-1", "isp:ord-1:pole-1", "pole-1", Owner, [OccupancyChangeBuilder.Add(2)], null, "crew-1", Created),
            CancellationToken.None);
        var againstRemoteOnlyOrder = () => stack.ChangeSets.StageAsync(
            OccupancyChangeSet.Stage("remote-only", "isp:remote-only:pole-1", "pole-1", Owner, [OccupancyChangeBuilder.Add(2)], null, "crew-1", Created),
            CancellationToken.None);

        await againstRemoteRowOfTheSameOrder.Should().ThrowAsync<KeyNotFoundException>();
        await againstRemoteOnlyOrder.Should().ThrowAsync<KeyNotFoundException>();
        (await stack.NeutralOperator.OccupancyChangeSets.IgnoreQueryFilters().AsNoTracking().CountAsync()).Should().Be(0, "nothing was written for either");
        var staged = await stack.ChangeSets.StageAsync(NewSet("ord-1", "pole-1", Created), CancellationToken.None);
        (await stack.ChangeSets.GetStagedAsync("ord-1", "ord-1:pole-1", CancellationToken.None))!.Id.Should().Be(staged.Id, "the kernel row of the same order and pole is a target");
    }
}
