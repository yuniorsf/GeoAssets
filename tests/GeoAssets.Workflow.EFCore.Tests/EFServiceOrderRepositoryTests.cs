using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Selection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public class EFServiceOrderRepositoryTests
{
    private static ServiceOrder Order(
        string id,
        string status = ServiceOrderStatus.Draft,
        string createdBy = "u1",
        string? assignedTo = null,
        string orderTypeId = "inspection",
        string? parentOrderId = null,
        DateTime? createdAt = null,
        byte[]? rowVersion = null) => new()
    {
        Id            = id,
        RowVersion    = rowVersion ?? [],
        Status        = status,
        CreatedBy     = createdBy,
        AssignedTo    = assignedTo,
        OrderTypeId   = orderTypeId,
        ParentOrderId = parentOrderId,
        CreatedAt     = createdAt ?? DateTime.UtcNow,
    };

    private sealed class FakeServiceOrder : IServiceOrder
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public byte[] RowVersion { get; init; } = [];
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string OrderTypeId { get; init; } = string.Empty;
        public string Status { get; init; } = ServiceOrderStatus.Draft;
        public ServiceOrderPriority Priority { get; init; } = ServiceOrderPriority.Normal;
        public string CreatedBy { get; init; } = string.Empty;
        public string? AssignedTo { get; init; }
        public Guid OrganizationId { get; init; } = Guid.Empty;
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; init; }
        public DateTime? ScheduledAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
        public IReadOnlyList<GeoFeature> Features { get; init; } = [];
        public FeatureSelectionSpec? SelectionSpec { get; init; }
        public string? ParentOrderId { get; init; }
        public IReadOnlyList<string> ChildOrderIds { get; init; } = [];
        public IReadOnlyList<OrderDispatch> Dispatches { get; init; } = [];
        public IReadOnlyList<OrderActionLog> ActionLog { get; init; } = [];
    }

    // ── AddAsync / GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsScalarFields()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var order = Order("a", assignedTo: "tech-1");
        order.Title = "Inspect valve";
        order.Description = "Routine check";

        await repo.AddAsync(order);
        var loaded = await repo.GetByIdAsync("a");

        loaded.Should().NotBeNull();
        loaded!.Title.Should().Be("Inspect valve");
        loaded.Description.Should().Be("Routine check");
        loaded.OrderTypeId.Should().Be("inspection");
        loaded.Status.Should().Be(ServiceOrderStatus.Draft);
        loaded.CreatedBy.Should().Be("u1");
        loaded.AssignedTo.Should().Be("tech-1");
    }

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsOrganizationId()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var orgId = Guid.NewGuid();

        await repo.AddAsync(new ServiceOrder
        {
            Id             = "org-1",
            CreatedBy      = "u1",
            OrderTypeId    = "inspection",
            OrganizationId = orgId,
        });
        var loaded = await repo.GetByIdAsync("org-1");

        loaded.Should().NotBeNull();
        loaded!.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public async Task AddAsync_FiresOrderAddedEvent()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        IServiceOrder? raised = null;
        repo.OrderAdded += (_, o) => raised = o;

        await repo.AddAsync(Order("a"));

        raised.Should().NotBeNull();
        raised!.Id.Should().Be("a");
    }

    [Fact]
    public async Task AddAsync_NonServiceOrderImplementation_ThrowsArgumentException()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);

        var act = () => repo.AddAsync(new FakeServiceOrder { Id = "a" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);

        (await repo.GetByIdAsync("missing")).Should().BeNull();
    }

    // ── Feature hydration (ServiceOrderMapper) ─────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WithAssetProvider_HydratesFeatures()
    {
        using var fixture = new SqliteFixture();
        var assets = new TestAssetProvider();
        assets.Add(new GeoFeature { Id = "f1", Geometry = new GeoPoint(0, 0) });
        var repo = new EFServiceOrderRepository(fixture.Context, assets);

        var order = Order("a");
        order.Features.Add(new GeoFeature { Id = "f1", Geometry = new GeoPoint(0, 0) });
        await repo.AddAsync(order);

        var loaded = await repo.GetByIdAsync("a");

        loaded!.Features.Should().ContainSingle(f => f.Id == "f1");
    }

    [Fact]
    public async Task GetByIdAsync_WithoutAssetProvider_FeatureIdsPreservedButFeaturesEmpty()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context); // no IAssetProvider

        var order = Order("a");
        order.Features.Add(new GeoFeature { Id = "f1", Geometry = new GeoPoint(0, 0) });
        await repo.AddAsync(order);

        var loaded = (ServiceOrder)(await repo.GetByIdAsync("a"))!;

        loaded.Features.Should().BeEmpty();
        loaded.FeatureIds.Should().ContainSingle(id => id == "f1");
    }

    [Fact]
    public async Task AddAsync_WithSelectionSpec_RoundTrips()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var order = Order("a");
        order.SelectionSpec = new FeatureSelectionSpec { StrategyId = "bounding-box", Note = "north sector", ExecutedAt = TimeProvider.System.GetUtcNow().UtcDateTime };

        await repo.AddAsync(order);
        var loaded = await repo.GetByIdAsync("a");

        loaded!.SelectionSpec.Should().NotBeNull();
        loaded.SelectionSpec!.StrategyId.Should().Be("bounding-box");
        loaded.SelectionSpec.Note.Should().Be("north sector");
    }

    [Fact]
    public async Task AddAsync_NoSelectionSpec_RoundTripsAsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);

        await repo.AddAsync(Order("a"));
        var loaded = await repo.GetByIdAsync("a");

        loaded!.SelectionSpec.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WithAttributes_RoundTrips()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var order = Order("a");
        order.Attributes["severity"] = "3";

        await repo.AddAsync(order);
        var loaded = await repo.GetByIdAsync("a");

        loaded!.Attributes.Should().ContainKey("severity").WhoseValue.Should().Be("3");
    }

    [Fact]
    public async Task GetByIdAsync_CorruptAttributesJson_FallsBackToEmptyDictionary()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a"));

        // Simulate a corrupted column value from outside the mapper's own writes —
        // ServiceOrderMapper.ToRecord always produces valid JSON, so this is the only
        // way to exercise DeserializeOrDefault's catch-fallback branch.
        await fixture.Context.Database.ExecuteSqlRawAsync(
            "UPDATE ServiceOrders SET AttributesJson = '{{not json' WHERE Id = 'a'");

        var loaded = await repo.GetByIdAsync("a");

        loaded!.Attributes.Should().BeEmpty();
    }

    // ── Hierarchy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRootsAsync_ReturnsOnlyOrdersWithNoParent()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("root"));
        await repo.AddAsync(Order("child", parentOrderId: "root"));

        var roots = await repo.GetRootsAsync();

        roots.Select(o => o.Id).Should().BeEquivalentTo(["root"]);
    }

    [Fact]
    public async Task GetChildrenAsync_ReturnsDirectChildrenOnly()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("root"));
        await repo.AddAsync(Order("child", parentOrderId: "root"));
        await repo.AddAsync(Order("grandchild", parentOrderId: "child"));

        var children = await repo.GetChildrenAsync("root");

        children.Select(o => o.Id).Should().BeEquivalentTo(["child"]);
    }

    [Fact]
    public async Task GetByIdAsync_PopulatesChildOrderIds()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("root"));
        await repo.AddAsync(Order("child", parentOrderId: "root"));

        var root = await repo.GetByIdAsync("root");

        root!.ChildOrderIds.Should().BeEquivalentTo(["child"]);
    }

    [Fact]
    public async Task GetParentAsync_ReturnsParent()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("root"));
        await repo.AddAsync(Order("child", parentOrderId: "root"));

        var parent = await repo.GetParentAsync("child");

        parent!.Id.Should().Be("root");
    }

    [Fact]
    public async Task GetParentAsync_RootOrder_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("root"));

        (await repo.GetParentAsync("root")).Should().BeNull();
    }

    // ── Filtered queries ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByStatusAsync_FiltersByStatus()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        await repo.AddAsync(Order("b", status: ServiceOrderStatus.Pending));

        var pending = await repo.GetByStatusAsync(ServiceOrderStatus.Pending);

        pending.Select(o => o.Id).Should().BeEquivalentTo(["b"]);
    }

    [Fact]
    public async Task GetByAssigneeAsync_FiltersByAssignee()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", assignedTo: "tech-1"));
        await repo.AddAsync(Order("b", assignedTo: "tech-2"));

        var forTech1 = await repo.GetByAssigneeAsync("tech-1");

        forTech1.Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetByCreatorAsync_FiltersByCreator()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", createdBy: "sup-1"));
        await repo.AddAsync(Order("b", createdBy: "sup-2"));

        var bySup1 = await repo.GetByCreatorAsync("sup-1");

        bySup1.Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetByOrderTypeAsync_FiltersByOrderType()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", orderTypeId: "inspection"));
        await repo.AddAsync(Order("b", orderTypeId: "maintenance"));

        var maintenance = await repo.GetByOrderTypeAsync("maintenance");

        maintenance.Select(o => o.Id).Should().BeEquivalentTo(["b"]);
    }

    [Fact]
    public async Task GetByDateRangeAsync_FiltersByCreatedAt()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var inRange = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var outOfRange = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddAsync(Order("a", createdAt: inRange));
        await repo.AddAsync(Order("b", createdAt: outOfRange));

        var results = await repo.GetByDateRangeAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        results.Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetDispatchedToAsync_FiltersByDispatchTarget()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a"));
        await repo.AddAsync(Order("b"));
        await repo.AppendDispatchAsync("a", new OrderDispatch("crew-1", DispatchTargetType.Group, "sup-1", DateTime.UtcNow));

        var dispatchedToCrew1 = await repo.GetDispatchedToAsync("crew-1", DispatchTargetType.Group);

        dispatchedToCrew1.Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllOrdersOrderedByCreatedAt()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var early = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddAsync(Order("b", createdAt: late));
        await repo.AddAsync(Order("a", createdAt: early));

        var all = await repo.GetAllAsync();

        all.Select(o => o.Id).Should().Equal("a", "b");
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_LegalTransition_PersistsAndFiresEvents()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        var loaded = (ServiceOrder)(await repo.GetByIdAsync("a"))!;
        loaded.Status = ServiceOrderStatus.Pending;

        IServiceOrder? updatedRaised = null;
        (IServiceOrder Order, string Previous)? statusChangedRaised = null;
        repo.OrderUpdated += (_, o) => updatedRaised = o;
        repo.OrderStatusChanged += (_, e) => statusChangedRaised = e;

        await repo.UpdateAsync(loaded);

        updatedRaised.Should().NotBeNull();
        statusChangedRaised.Should().NotBeNull();
        statusChangedRaised!.Value.Previous.Should().Be(ServiceOrderStatus.Draft);
        (await repo.GetByIdAsync("a"))!.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task UpdateAsync_SameStatus_DoesNotFireOrderStatusChanged()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a"));
        var loaded = (ServiceOrder)(await repo.GetByIdAsync("a"))!;
        loaded.Title = "Renamed";

        var fired = false;
        repo.OrderStatusChanged += (_, _) => fired = true;

        await repo.UpdateAsync(loaded);

        fired.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_IllegalTransition_ThrowsAndDoesNotPersist()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        var loaded = (ServiceOrder)(await repo.GetByIdAsync("a"))!;
        loaded.Status = ServiceOrderStatus.Completed;

        var act = () => repo.UpdateAsync(loaded);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        (await repo.GetByIdAsync("a"))!.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task UpdateAsync_UnknownOrder_ThrowsKeyNotFoundException()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);

        var act = () => repo.UpdateAsync(Order("missing"));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_NonServiceOrderImplementation_ThrowsArgumentException()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);

        var act = () => repo.UpdateAsync(new FakeServiceOrder { Id = "a" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentWriters_ThrowsServiceOrderConcurrencyException()
    {
        using var fixture = new SqliteFixture();
        var repoA = new EFServiceOrderRepository(fixture.Context);
        await repoA.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        // Writer B loads (and tracks, with its own RowVersion snapshot) before writer A saves.
        using var contextB = fixture.NewContext();
        var repoB = new EFServiceOrderRepository(contextB);
        var orderB = (ServiceOrder)(await repoB.GetByIdAsync("a"))!;

        // Writer A updates and saves first — bumps RowVersion in the database.
        var orderA = (ServiceOrder)(await repoA.GetByIdAsync("a"))!;
        orderA.Title = "Changed by A";
        await repoA.UpdateAsync(orderA);

        // Writer B's save now targets a RowVersion the database no longer has.
        orderB.Title = "Changed by B";
        var act = () => repoB.UpdateAsync(orderB);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()
            .Where(e => e.OrderId == "a");
    }

    /// <summary>
    /// Unlike <see cref="UpdateAsync_ConcurrentWriters_ThrowsServiceOrderConcurrencyException"/>
    /// (which incidentally catches its race only because both calls share one DbContext's change
    /// tracker), this reads the stale copy through a context that is disposed long before the
    /// later write — the same shape as two separate HTTP requests (or a UI panel held open across
    /// an arbitrary gap) each getting their own scoped DbContext. Proves XD01-26's actual target
    /// scenario: without round-tripping RowVersion, this save would succeed and silently overwrite
    /// the other writer's change.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_StaleRowVersionFromLongDisposedContext_ThrowsServiceOrderConcurrencyException()
    {
        using var fixture = new SqliteFixture();
        var seedRepo = new EFServiceOrderRepository(fixture.Context);
        await seedRepo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        byte[] staleRowVersion;
        using (var readerContext = fixture.NewContext())
        {
            var read = (ServiceOrder)(await new EFServiceOrderRepository(readerContext).GetByIdAsync("a"))!;
            staleRowVersion = read.RowVersion;
        }
        // readerContext is disposed here — nothing about the later write shares its tracker.

        using (var otherWriterContext = fixture.NewContext())
        {
            var otherWriterRepo = new EFServiceOrderRepository(otherWriterContext);
            var toUpdate = (ServiceOrder)(await otherWriterRepo.GetByIdAsync("a"))!;
            toUpdate.Title = "Changed by someone else";
            await otherWriterRepo.UpdateAsync(toUpdate);
        }

        using var lateContext = fixture.NewContext();
        var lateRepo = new EFServiceOrderRepository(lateContext);
        var staleSnapshot = Order("a", status: ServiceOrderStatus.Draft, rowVersion: staleRowVersion);

        var act = () => lateRepo.UpdateAsync(staleSnapshot);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()
            .Where(e => e.OrderId == "a");
    }

    [Fact]
    public async Task UpdateAsync_FreshRowVersionFromSeparateContext_Succeeds()
    {
        using var fixture = new SqliteFixture();
        var seedRepo = new EFServiceOrderRepository(fixture.Context);
        await seedRepo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        using var readerContext = fixture.NewContext();
        var currentRowVersion = (await new EFServiceOrderRepository(readerContext).GetByIdAsync("a"))!.RowVersion;

        using var writerContext = fixture.NewContext();
        var writerRepo = new EFServiceOrderRepository(writerContext);
        var freshSnapshot = Order("a", status: ServiceOrderStatus.Pending, rowVersion: currentRowVersion);

        await writerRepo.UpdateAsync(freshSnapshot);

        var reloaded = await writerRepo.GetByIdAsync("a");
        reloaded!.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    // ── AppendDispatchAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AppendDispatchAsync_AppendsDispatchAndFiresOrderUpdated()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a"));
        IServiceOrder? raised = null;
        repo.OrderUpdated += (_, o) => raised = o;

        await repo.AppendDispatchAsync("a", new OrderDispatch("tech-1", DispatchTargetType.User, "sup-1", DateTime.UtcNow, "please go"));

        raised.Should().NotBeNull();
        var loaded = await repo.GetByIdAsync("a");
        loaded!.Dispatches.Should().ContainSingle(d => d.TargetId == "tech-1" && d.Note == "please go");
    }

    [Fact]
    public async Task AppendDispatchAsync_UnknownOrder_ThrowsKeyNotFoundException()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);

        var act = () => repo.AppendDispatchAsync("missing", new OrderDispatch("tech-1", DispatchTargetType.User, "sup-1", DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── AppendActionAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task AppendActionAsync_NoResultingStatus_AppendsWithoutChangingStatus()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        await repo.AppendActionAsync("a", new OrderActionLog(OrderActionType.Annotate, "tech-1", DateTime.UtcNow, "note"));

        var loaded = await repo.GetByIdAsync("a");
        loaded!.Status.Should().Be(ServiceOrderStatus.Draft);
        loaded.ActionLog.Should().ContainSingle(a => a.Comment == "note");
    }

    [Fact]
    public async Task AppendActionAsync_LegalResultingStatus_TransitionsAndFiresStatusChanged()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        (IServiceOrder Order, string Previous)? raised = null;
        repo.OrderStatusChanged += (_, e) => raised = e;

        await repo.AppendActionAsync("a", new OrderActionLog(
            OrderActionType.Dispatch, "sup-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Pending));

        raised.Should().NotBeNull();
        raised!.Value.Previous.Should().Be(ServiceOrderStatus.Draft);
        (await repo.GetByIdAsync("a"))!.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task AppendActionAsync_ToCompletedState_StampsCompletedAt()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", status: ServiceOrderStatus.InProgress));

        await repo.AppendActionAsync("a", new OrderActionLog(
            OrderActionType.Complete, "tech-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Completed));

        (await repo.GetByIdAsync("a"))!.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AppendActionAsync_IllegalResultingStatus_ThrowsAndDoesNotAppend()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        var act = () => repo.AppendActionAsync("a", new OrderActionLog(
            OrderActionType.Complete, "tech-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        (await repo.GetByIdAsync("a"))!.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendActionAsync_UnknownOrder_ThrowsKeyNotFoundException()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);

        var act = () => repo.AppendActionAsync("missing", new OrderActionLog(OrderActionType.Annotate, "tech-1", DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesOrderAndFiresEvent()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a"));
        string? raised = null;
        repo.OrderDeleted += (_, id) => raised = id;

        await repo.DeleteAsync("a");

        raised.Should().Be("a");
        (await repo.GetByIdAsync("a")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_IsNoOp()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var fired = false;
        repo.OrderDeleted += (_, _) => fired = true;

        await repo.DeleteAsync("missing");

        fired.Should().BeFalse();
    }
}
