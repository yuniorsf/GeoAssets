using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using GeoAssets.Workflow.Tests.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator.SharedUse;

/// <summary>
/// What <see cref="SharedUseDispatchService"/> does beyond the shared contract suite
/// (<see cref="InMemorySharedUseDispatchContractTests"/>): its argument guards, the order of its
/// checks, the callers it refuses, the order types it cannot fan out, how it writes, and its
/// registration (XD01-316, DISP-G1..G5, DISP-G10, DISP-B9).
/// </summary>
public sealed class SharedUseDispatchServiceTests
{
    private const string Order = "ord-1";
    private const string PoleType = "pole";

    private static readonly Guid Owner = SharedUseDispatchFixture.Owner;
    private static readonly Guid Occupant = new("0c0c0c0c-0000-4000-8000-000000000001");
    private static readonly Guid OtherOccupant = new("0c0c0c0c-0000-4000-8000-000000000002");

    private static readonly WorkflowPrincipal Admin = new("dispatcher-1", Owner.ToString(), ["Administrator"], [], []);

    private readonly SharedUseDispatchFixture _fixture = new();

    // ── Guards (DISP-G1) ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCollaborator_ThrowsArgumentNullException()
    {
        var f = _fixture;
        var outbox = new RecordingOutboxWriter();
        var unit = new InMemoryWorkflowUnitOfWork();

        Action[] constructions =
        [
            () => _ = new SharedUseDispatchService(null!, f.OrderTypes, f.Rules, f.Targets, f.Targets, f.Occupancy, outbox, unit, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, null!, f.Rules, f.Targets, f.Targets, f.Occupancy, outbox, unit, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, f.OrderTypes, null!, f.Targets, f.Targets, f.Occupancy, outbox, unit, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, f.OrderTypes, f.Rules, null!, f.Targets, f.Occupancy, outbox, unit, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, f.OrderTypes, f.Rules, f.Targets, null!, f.Occupancy, outbox, unit, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Targets, null!, outbox, unit, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Targets, f.Occupancy, null!, unit, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Targets, f.Occupancy, outbox, null!, f.Clock),
            () => _ = new SharedUseDispatchService(f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Targets, f.Occupancy, outbox, unit, null!),
        ];

        constructions.Should().AllSatisfy(construct => construct.Should().Throw<ArgumentNullException>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EveryMember_BlankOrderId_ThrowsArgumentException(string orderId)
    {
        var sut = _fixture.Sut;

        await ((Func<Task>)(() => sut.DispatchAsync(orderId, [], Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.PreviewAsync(orderId, 1, 50, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.GetSkippedEntriesAsync(orderId, 1, 50, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.GetUnnotifiablePolesAsync(orderId, 1, 50, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EveryMember_NullOrderIdPrincipalOrRowVersion_ThrowsArgumentNullException()
    {
        var sut = _fixture.Sut;

        await ((Func<Task>)(() => sut.DispatchAsync(null!, [], Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.DispatchAsync(Order, null!, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.DispatchAsync(Order, [], null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.PreviewAsync(Order, 1, 50, null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.GetSkippedEntriesAsync(Order, 1, 50, null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.GetUnnotifiablePolesAsync(Order, 1, 50, null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PlanReads_CheckPagingBeforeTheOrderIsEvenLookedUp()
    {
        var sut = _fixture.Sut;

        await ((Func<Task>)(() => sut.PreviewAsync("ord-missing", 0, 50, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetSkippedEntriesAsync("ord-missing", 1, 201, Admin, CancellationToken.None))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ── Callers (DISP-G2, DISP-G3, owner only) ────────────────────────────────

    [Fact]
    public async Task EveryMember_UnknownOrder_ThrowsKeyNotFoundException()
    {
        var sut = _fixture.Sut;

        await ((Func<Task>)(() => sut.DispatchAsync("ord-missing", [], Admin, CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
        await ((Func<Task>)(() => sut.PreviewAsync("ord-missing", 1, 50, Admin, CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task EveryMember_ByAGranteeAllowedToDispatch_IsRefusedAsOwnerOnly_BeforeAnyStoreIsRead()
    {
        await SeedAsync();
        var grantee = new WorkflowPrincipal("grantee-1", Occupant.ToString(), ["Administrator"], [], [])
        {
            OrgGrants = [new WorkflowOrgGrant(Owner.ToString(), "ServiceOrder", ["serviceorders:view", "serviceorders:dispatch"], RequiredRole: null)],
        };
        _fixture.Rules.Evaluate(grantee, OrderActionType.Dispatch, (await _fixture.Orders.GetByIdAsync(Order))!).Allowed
            .Should().BeTrue("the grant is live: the rules alone would let the grantee dispatch");
        var untouchable = new UntouchableTargetStore();
        var sut = _fixture.Build(targets: untouchable, targetWriter: untouchable);

        var dispatch = () => sut.DispatchAsync(Order, [], grantee, CancellationToken.None);
        var preview = () => sut.PreviewAsync(Order, 1, 50, grantee, CancellationToken.None);
        var skipped = () => sut.GetSkippedEntriesAsync(Order, 1, 50, grantee, CancellationToken.None);
        var poles = () => sut.GetUnnotifiablePolesAsync(Order, 1, 50, grantee, CancellationToken.None);

        var denied = (await dispatch.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Evaluation!.Reason.Should().Be(SharedUseDispatchService.OwnerOnlyReason);
        denied.Evaluation.IsOutsideOrganization.Should().BeFalse("the grantee is inside the gate; it is the dispatch that is the owner's alone");
        await preview.Should().ThrowAsync<OrderActionDeniedException>();
        await skipped.Should().ThrowAsync<OrderActionDeniedException>();
        await poles.Should().ThrowAsync<OrderActionDeniedException>();
        untouchable.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_ByTheRecipientOfAChild_OnThatChild_IsDenied()
    {
        await SeedAsync();
        await _fixture.Sut.DispatchAsync(Order, [], Admin, CancellationToken.None);
        var childId = SharedUseDispatchPlanner.ChildOrderId(Order, Occupant);
        var recipient = new WorkflowPrincipal("occupant-admin", Occupant.ToString(), ["Supervisor"], [], []);

        var act = () => _fixture.Sut.DispatchAsync(childId, [], recipient, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.Reason
            .Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason, "XD01-333: the notice type only informs its occupant, which the rules refuse before the owner-only check");
    }

    // ── Order types (DISP-G4, DISP-G5) ────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_OrderOfAnUnregisteredType_ThrowsOrderTypeNotRegistered_NamingTheType()
    {
        await _fixture.Orders.AddAsync(NewOrder("ghost-type"));

        var act = () => _fixture.Sut.DispatchAsync(Order, [], Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderTypeNotRegisteredException>()).Which.OrderTypeId.Should().Be("ghost-type");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchAsync_OrderWhoseTypeDeclaresNoFanOut_ThrowsOrderTypeHasNoFanOut(bool withTargetPolicy)
    {
        _fixture.OrderTypes.Register(new OrderType
        {
            Id = "plain",
            DisplayName = "Plain",
            TargetPolicy = withTargetPolicy ? new OrderTargetPolicy { TargetAssetTypeIds = [PoleType] } : null,
        });
        await _fixture.Orders.AddAsync(NewOrder("plain"));

        var dispatch = () => _fixture.Sut.DispatchAsync(Order, [], Admin, CancellationToken.None);
        var preview = () => _fixture.Sut.PreviewAsync(Order, 1, 50, Admin, CancellationToken.None);

        (await dispatch.Should().ThrowAsync<OrderTypeHasNoFanOutException>()).Which.OrderTypeId.Should().Be("plain");
        await preview.Should().ThrowAsync<OrderTypeHasNoFanOutException>();
    }

    [Fact]
    public async Task DispatchAsync_FanOutChildTypeNotRegistered_ThrowsOrderTypeNotRegistered_NamingTheChildType()
    {
        _fixture.OrderTypes.Register(SharedUseOrderTypes.CreatePoleWork([PoleType]));
        await _fixture.Orders.AddAsync(NewOrder(SharedUseOrderTypes.PoleWorkId));

        var act = () => _fixture.Sut.DispatchAsync(Order, [], Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderTypeNotRegisteredException>()).Which.OrderTypeId.Should().Be(SharedUseOrderTypes.OccupantNoticeId);
    }

    [Fact]
    public async Task DispatchAsync_AStatusWithNoDispatch_IsRefusedBeforeTargetsOrOccupancyAreRead()
    {
        SharedUseOrderTypes.RegisterAll(_fixture.OrderTypes, [PoleType]);
        var order = NewOrder(SharedUseOrderTypes.PoleWorkId);
        order.Status = ServiceOrderStatus.Completed;
        await _fixture.Orders.AddAsync(order);
        var untouchable = new UntouchableTargetStore();

        var act = () => _fixture.Build(targets: untouchable, targetWriter: untouchable).DispatchAsync(Order, [], Admin, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        untouchable.Calls.Should().Be(0);
    }

    // ── The write (DISP-G10, DISP-B9) ─────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_WritesTheParentFirst_CarryingTheCallersRowVersionAsItsPrecondition()
    {
        await SeedAsync();
        var orders = new RecordingOrders(_fixture.Orders);
        byte[] rowVersion = [1, 2, 3, 4];

        await _fixture.Build(orders: orders).DispatchAsync(Order, rowVersion, Admin, CancellationToken.None);

        orders.Writes[0].Should().Be($"update:{Order}");
        orders.UpdatedRowVersions.Should().ContainSingle().Which.Should().Equal(rowVersion);
        orders.Writes.Should().Equal(
            $"update:{Order}",
            $"add:{SharedUseDispatchPlanner.ChildOrderId(Order, Occupant)}",
            $"add:{SharedUseDispatchPlanner.ChildOrderId(Order, OtherOccupant)}",
            $"action:{Order}");
    }

    [Fact]
    public async Task DispatchAsync_AStaleRowVersion_SurfacesAsServiceOrderConcurrencyException_AndCreatesNoChild()
    {
        await SeedAsync();
        var orders = new RecordingOrders(_fixture.Orders) { FailUpdateWith = new ServiceOrderConcurrencyException(Order) };

        var act = () => _fixture.Build(orders: orders).DispatchAsync(Order, [9], Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be(Order);
        (await _fixture.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
        _fixture.Outbox.Messages.Should().BeEmpty();
        _fixture.UnitOfWork.Completed.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_AChildCreatedByAnotherDispatchBetweenThePlanAndTheWrite_IsLeftToIt()
    {
        await SeedAsync();
        var raced = SharedUseDispatchPlanner.ChildOrderId(Order, Occupant);
        var orders = new RecordingOrders(_fixture.Orders)
        {
            // The other dispatch commits its child while this one waits for the parent.
            BeforeUpdateAsync = () => _fixture.Orders.AddAsync(new ServiceOrder
            {
                Id = raced,
                Title = "from the other dispatch",
                OrderTypeId = SharedUseOrderTypes.OccupantNoticeId,
                ParentOrderId = Order,
                OrganizationId = Owner,
            }),
        };

        var result = await _fixture.Build(orders: orders).DispatchAsync(Order, [], Admin, CancellationToken.None);

        result.ChildOrdersCreated.Should().Be(1);
        result.ChildOrdersAlreadyDispatched.Should().Be(1);
        result.NotificationsEnqueued.Should().Be(1);
        (await _fixture.Orders.GetByIdAsync(raced))!.Title.Should().Be("from the other dispatch", "it was not created a second time");
        _fixture.Outbox.Messages.Should().ContainSingle().Which.PartitionKey.Should().Be(SharedUseDispatchPlanner.ChildOrderId(Order, OtherOccupant));
        orders.Writes.Should().NotContain($"add:{raced}");
    }

    [Fact]
    public async Task DispatchAsync_EveryMissingChildCreatedByAnotherDispatchInBetween_CreatesNothingAndLogsNothing()
    {
        await SeedAsync(OccupantOnly: true);
        var raced = SharedUseDispatchPlanner.ChildOrderId(Order, Occupant);
        var orders = new RecordingOrders(_fixture.Orders)
        {
            BeforeUpdateAsync = () => _fixture.Orders.AddAsync(new ServiceOrder { Id = raced, ParentOrderId = Order, OrganizationId = Owner }),
        };

        var result = await _fixture.Build(orders: orders).DispatchAsync(Order, [], Admin, CancellationToken.None);

        result.ChildOrdersCreated.Should().Be(0);
        result.ChildOrdersAlreadyDispatched.Should().Be(1);
        orders.Writes.Should().Equal($"update:{Order}");
        _fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_StagesEachChildsNotificationWhileThatChildsSpanIsOpen()
    {
        await SeedAsync();
        var tracer = new RecordingTracer();
        var outbox = new RecordingOutboxWriter
        {
            OnEnqueueAsync = (message, _) =>
            {
                tracer.Open.Should().Equal([message.PartitionKey], "the notification is staged inside its own child's span");
                return Task.CompletedTask;
            },
        };

        await _fixture.Build(outbox: outbox, tracer: tracer).DispatchAsync(Order, [], Admin, CancellationToken.None);

        outbox.Messages.Should().HaveCount(2);
        tracer.Started.Should().Equal(
            (Order, SharedUseDispatchPlanner.ChildOrderId(Order, Occupant), Occupant),
            (Order, SharedUseDispatchPlanner.ChildOrderId(Order, OtherOccupant), OtherOccupant));
        tracer.Open.Should().BeEmpty("every child span was closed");
    }

    [Fact]
    public async Task DispatchAsync_AFailureWhileStagingANotification_Propagates_AndTheUnitOfWorkIsNotCompleted()
    {
        await SeedAsync();
        var outbox = new RecordingOutboxWriter
        {
            OnEnqueueAsync = (_, _) => throw new InvalidOperationException("outbox is down"),
        };

        var act = () => _fixture.Build(outbox: outbox).DispatchAsync(Order, [], Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("outbox is down");
        _fixture.UnitOfWork.Started.Should().Be(1);
        _fixture.UnitOfWork.Completed.Should().Be(0, "whether the stores keep anything is the unit of work's to decide — see the EF Core tests");
    }

    [Fact]
    public async Task DispatchAsync_AnOrderThatIsNotAServiceOrder_ThrowsInvalidOperationException_BeforeAnyWrite()
    {
        await SeedAsync();
        var orders = new RecordingOrders(_fixture.Orders) { Wrap = true };

        var act = () => _fixture.Build(orders: orders).DispatchAsync(Order, [], Admin, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        orders.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_CarriesTheOrdersRowVersion_ForTheDispatchThatFollows()
    {
        await SeedAsync(rowVersion: [7, 7, 7]);

        var preview = await _fixture.Sut.PreviewAsync(Order, 1, 50, Admin, CancellationToken.None);

        preview.RowVersion.Should().Equal(7, 7, 7);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public void AddSharedUseDispatch_RegistersOneScopedServiceBehindBothPorts()
    {
        var services = HostServices();
        services.AddSharedUseDispatch();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        using var other = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ISharedUseDispatchService>();

        service.Should().BeOfType<SharedUseDispatchService>();
        scope.ServiceProvider.GetRequiredService<ISharedUseDispatchPlanReader>().Should().BeSameAs(service);
        scope.ServiceProvider.GetRequiredService<SharedUseDispatchService>().Should().BeSameAs(service);
        other.ServiceProvider.GetRequiredService<ISharedUseDispatchService>().Should().NotBeSameAs(service);
    }

    [Fact]
    public void AddSharedUseDispatch_CalledTwice_KeepsOneRegistrationPerPort_AndNullServicesThrows()
    {
        var services = HostServices();
        services.AddSharedUseDispatch().AddSharedUseDispatch();

        services.Count(d => d.ServiceType == typeof(ISharedUseDispatchService)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(ISharedUseDispatchPlanReader)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(SharedUseDispatchService)).Should().Be(1);
        ((Action)(() => WorkflowServiceExtensions.AddSharedUseDispatch(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSharedUseDispatch_WithNoUnitOfWorkRegistered_FailsWhenTheServiceIsResolved()
    {
        var services = HostServices(withUnitOfWork: false);
        services.AddSharedUseDispatch();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ISharedUseDispatchService>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IWorkflowUnitOfWork*");
    }

    // ── Arrange ───────────────────────────────────────────────────────────────

    private static ServiceCollection HostServices(bool withUnitOfWork = true)
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        services.AddServiceOrderRules();
        services.AddScoped<IServiceOrderRepository, FakeServiceOrderRepository>();
        services.AddScoped(sp => new InMemoryServiceOrderTargetStore(sp.GetRequiredService<IServiceOrderRepository>()));
        services.AddScoped<IServiceOrderTargetReader>(sp => sp.GetRequiredService<InMemoryServiceOrderTargetStore>());
        services.AddScoped<IServiceOrderTargetWriter>(sp => sp.GetRequiredService<InMemoryServiceOrderTargetStore>());
        services.AddScoped<IPoleOccupancyReader>(_ => new InMemoryPoleOccupancyStore());
        services.AddScoped<IOutboxWriter, RecordingOutboxWriter>();
        if (withUnitOfWork)
            services.AddScoped<IWorkflowUnitOfWork, InMemoryWorkflowUnitOfWork>();
        return services;
    }

    private static ServiceOrder NewOrder(string orderTypeId, byte[]? rowVersion = null) => new()
    {
        Id = Order,
        RowVersion = rowVersion ?? [],
        Title = "Shared use",
        OrderTypeId = orderTypeId,
        CreatedBy = "planner-1",
        OrganizationId = Owner,
        CreatedAt = SharedUseDispatchFixture.Now.UtcDateTime.AddDays(-1),
    };

    /// <summary>A draft pole-work order on one pole occupied by <see cref="Occupant"/> and, unless asked otherwise, <see cref="OtherOccupant"/>.</summary>
    private async Task SeedAsync(bool OccupantOnly = false, byte[]? rowVersion = null)
    {
        SharedUseOrderTypes.RegisterAll(_fixture.OrderTypes, [PoleType]);
        await _fixture.Orders.AddAsync(NewOrder(SharedUseOrderTypes.PoleWorkId, rowVersion));
        await _fixture.Targets.AddRangeAsync(
            Order,
            [new ServiceOrderTargetBuilder()
                .WithId(OrderTargetSelectionService.TargetId(Order, "pole-1"))
                .WithOrderId(Order)
                .WithFeatureId("pole-1")
                .WithAssetTypeId(PoleType)
                .WithActivityIds("relocate-attachment")
                .WithOrganizationId(Owner)
                .Build()],
            CancellationToken.None);

        var entries = new List<PoleOccupancyEntry>
        {
            new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(Owner).OnSlot(1).OccupiedBy(Occupant).Build(),
        };
        if (!OccupantOnly)
            entries.Add(new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(Owner).OnSlot(2).OccupiedBy(OtherOccupant).Build());
        await _fixture.Occupancy.ReplaceEntriesAsync("pole-1", PoleOccupancy.UnsavedVersion, entries, CancellationToken.None);
    }

    /// <summary>Records which child spans are open and which were started, in order.</summary>
    private sealed class RecordingTracer : ISharedUseFanOutTracer
    {
        public List<(string Parent, string Child, Guid Occupant)> Started { get; } = [];

        public List<string> Open { get; } = [];

        public IDisposable? BeginChild(string parentOrderId, string childOrderId, Guid occupantOrganizationId)
        {
            Started.Add((parentOrderId, childOrderId, occupantOrganizationId));
            Open.Add(childOrderId);
            return new Span(this, childOrderId);
        }

        private sealed class Span(RecordingTracer owner, string childOrderId) : IDisposable
        {
            public void Dispose() => owner.Open.Remove(childOrderId);
        }
    }

    /// <summary>
    /// The fake order store, recording its writes in order — and able to fail the parent's update,
    /// to do something just before it, or to hand orders out as something other than
    /// <see cref="ServiceOrder"/>.
    /// </summary>
    private sealed class RecordingOrders(FakeServiceOrderRepository inner) : IServiceOrderRepository
    {
        public List<string> Writes { get; } = [];

        public List<byte[]> UpdatedRowVersions { get; } = [];

        public Exception? FailUpdateWith { get; init; }

        public Func<Task>? BeforeUpdateAsync { get; init; }

        public bool Wrap { get; init; }

        public event EventHandler<IServiceOrder>? OrderAdded { add => inner.OrderAdded += value; remove => inner.OrderAdded -= value; }
        public event EventHandler<IServiceOrder>? OrderUpdated { add => inner.OrderUpdated += value; remove => inner.OrderUpdated -= value; }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add => inner.OrderStatusChanged += value; remove => inner.OrderStatusChanged -= value; }
        public event EventHandler<string>? OrderDeleted { add => inner.OrderDeleted += value; remove => inner.OrderDeleted -= value; }

        public async Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var order = await inner.GetByIdAsync(id, ct);
            return Wrap && order is not null ? new ForeignOrder(order) : order;
        }

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => inner.GetRootsAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => inner.GetChildrenAsync(parentId, ct);
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => inner.GetParentAsync(childId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => inner.GetByStatusAsync(status, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => inner.GetByAssigneeAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => inner.GetByCreatorAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => inner.GetByOrderTypeAsync(orderTypeId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => inner.GetByDateRangeAsync(from, toExclusive, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => inner.GetDispatchedToAsync(targetId, targetType, ct);

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        {
            Writes.Add($"add:{order.Id}");
            return inner.AddAsync(order, ct);
        }

        public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            if (BeforeUpdateAsync is not null) await BeforeUpdateAsync();
            if (FailUpdateWith is not null) throw FailUpdateWith;

            Writes.Add($"update:{order.Id}");
            UpdatedRowVersions.Add(order.RowVersion);
            await inner.UpdateAsync(order, ct);
        }

        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
        {
            Writes.Add($"dispatch:{orderId}");
            return inner.AppendDispatchAsync(orderId, dispatch, ct);
        }

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            Writes.Add($"action:{orderId}");
            return inner.AppendActionAsync(orderId, entry, ct);
        }

        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
    }

    /// <summary>An <see cref="IServiceOrder"/> that is not a <see cref="ServiceOrder"/>.</summary>
    private sealed class ForeignOrder(IServiceOrder inner) : IServiceOrder
    {
        public string Id => inner.Id;
        public string Title => inner.Title;
        public string Description => inner.Description;
        public string OrderTypeId => inner.OrderTypeId;
        public byte[] RowVersion => inner.RowVersion;
        public string Status => inner.Status;
        public ServiceOrderPriority Priority => inner.Priority;
        public string CreatedBy => inner.CreatedBy;
        public string? AssignedTo => inner.AssignedTo;
        public DateTime CreatedAt => inner.CreatedAt;
        public DateTime? UpdatedAt => inner.UpdatedAt;
        public DateTime? ScheduledAt => inner.ScheduledAt;
        public DateTime? CompletedAt => inner.CompletedAt;
        public IReadOnlyDictionary<string, string> Attributes => inner.Attributes;
        public IReadOnlyList<GeoAssets.Core.Models.GeoFeature> Features => inner.Features;
        public GeoAssets.Workflow.Selection.FeatureSelectionSpec? SelectionSpec => inner.SelectionSpec;
        public string? ParentOrderId => inner.ParentOrderId;
        public IReadOnlyList<string> ChildOrderIds => inner.ChildOrderIds;
        public IReadOnlyList<OrderDispatch> Dispatches => inner.Dispatches;
        public IReadOnlyList<OrderActionLog> ActionLog => inner.ActionLog;
        public Guid OrganizationId => inner.OrganizationId;
    }
}
