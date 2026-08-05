using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class InMemoryServiceOrderRepositoryTests
{
    private static ServiceOrder Order(
        string id,
        string? parentOrderId = null,
        string status = ServiceOrderStatus.Draft,
        string createdBy = "",
        string? assignedTo = null,
        string orderTypeId = "",
        DateTime? createdAt = null) => new()
    {
        Id            = id,
        ParentOrderId = parentOrderId,
        Status        = status,
        CreatedBy     = createdBy,
        AssignedTo    = assignedTo,
        OrderTypeId   = orderTypeId,
        CreatedAt     = createdAt ?? DateTime.UtcNow,
    };

    private sealed class FakeServiceOrder : IServiceOrder
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string OrderTypeId { get; init; } = string.Empty;
        public string Status { get; init; } = ServiceOrderStatus.Draft;
        public ServiceOrderPriority Priority { get; init; } = ServiceOrderPriority.Normal;
        public string CreatedBy { get; init; } = string.Empty;
        public string? AssignedTo { get; init; }
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

    // ── GetById ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_MissingId_ReturnsNull()
    {
        (await new InMemoryServiceOrderRepository().GetByIdAsync("x")).Should().BeNull();
    }

    [Fact]
    public async Task GetById_ExistingOrder_ReturnsOrder()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));
        (await sut.GetByIdAsync("a"))!.Id.Should().Be("a");
    }

    [Fact]
    public async Task GetById_OrderWithChildren_RecomputesChildOrderIds()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("parent"));
        await sut.AddAsync(Order("child-a", parentOrderId: "parent"));
        await sut.AddAsync(Order("child-b", parentOrderId: "parent"));

        (await sut.GetByIdAsync("parent"))!.ChildOrderIds.Should().BeEquivalentTo(["child-a", "child-b"]);
    }

    // ── GetAll ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_EmptyStore_ReturnsEmpty()
    {
        (await new InMemoryServiceOrderRepository().GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_WithOrders_ReturnsAllMaterialized()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("parent"));
        await sut.AddAsync(Order("child", parentOrderId: "parent"));

        var all = await sut.GetAllAsync();

        all.Select(o => o.Id).Should().BeEquivalentTo(["parent", "child"]);
        all.Single(o => o.Id == "parent").ChildOrderIds.Should().BeEquivalentTo(["child"]);
    }

    // ── GetRoots ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRoots_MixOfRootAndChildOrders_ReturnsOnlyRoots()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("root"));
        await sut.AddAsync(Order("child", parentOrderId: "root"));

        (await sut.GetRootsAsync()).Select(o => o.Id).Should().BeEquivalentTo(["root"]);
    }

    // ── GetChildren ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetChildren_ParentWithChildren_ReturnsChildren()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("parent"));
        await sut.AddAsync(Order("child-a", parentOrderId: "parent"));
        await sut.AddAsync(Order("other-root"));

        (await sut.GetChildrenAsync("parent")).Select(o => o.Id).Should().BeEquivalentTo(["child-a"]);
    }

    [Fact]
    public async Task GetChildren_ParentWithNoChildren_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("parent"));

        (await sut.GetChildrenAsync("parent")).Should().BeEmpty();
    }

    // ── GetParent ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetParent_ChildIdNotFound_ReturnsNull()
    {
        (await new InMemoryServiceOrderRepository().GetParentAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetParent_RootOrder_ReturnsNull()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("root"));

        (await sut.GetParentAsync("root")).Should().BeNull();
    }

    [Fact]
    public async Task GetParent_ChildWithExistingParent_ReturnsParent()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("parent"));
        await sut.AddAsync(Order("child", parentOrderId: "parent"));

        (await sut.GetParentAsync("child"))!.Id.Should().Be("parent");
    }

    [Fact]
    public async Task GetParent_ChildWithMissingParentRecord_ReturnsNull()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("orphan", parentOrderId: "does-not-exist"));

        (await sut.GetParentAsync("orphan")).Should().BeNull();
    }

    // ── GetByStatus ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByStatus_MatchingOrders_ReturnsThem()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Pending));
        await sut.AddAsync(Order("b", status: ServiceOrderStatus.Draft));

        (await sut.GetByStatusAsync(ServiceOrderStatus.Pending)).Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetByStatus_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        (await sut.GetByStatusAsync(ServiceOrderStatus.Completed)).Should().BeEmpty();
    }

    // ── GetByAssignee ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByAssignee_MatchingOrder_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", assignedTo: "tech-1"));
        await sut.AddAsync(Order("b", assignedTo: "tech-2"));

        (await sut.GetByAssigneeAsync("tech-1")).Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetByAssignee_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", assignedTo: "tech-1"));

        (await sut.GetByAssigneeAsync("tech-9")).Should().BeEmpty();
    }

    // ── GetByCreator ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCreator_MatchingOrder_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", createdBy: "alice"));
        await sut.AddAsync(Order("b", createdBy: "bob"));

        (await sut.GetByCreatorAsync("alice")).Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetByCreator_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", createdBy: "alice"));

        (await sut.GetByCreatorAsync("carol")).Should().BeEmpty();
    }

    // ── GetByOrderType ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByOrderType_MatchingOrder_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", orderTypeId: "inspection"));
        await sut.AddAsync(Order("b", orderTypeId: "maintenance"));

        (await sut.GetByOrderTypeAsync("inspection")).Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetByOrderType_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", orderTypeId: "inspection"));

        (await sut.GetByOrderTypeAsync("emergency-repair")).Should().BeEmpty();
    }

    // ── GetByDateRange ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByDateRange_OrderWithinRange_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", createdAt: new DateTime(2026, 1, 15)));

        (await sut.GetByDateRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)))
            .Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetByDateRange_OrderBeforeRange_Excluded()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", createdAt: new DateTime(2025, 12, 31)));

        (await sut.GetByDateRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetByDateRange_OrderAfterRange_Excluded()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", createdAt: new DateTime(2026, 2, 1)));

        (await sut.GetByDateRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Should().BeEmpty();
    }

    // ── GetDispatchedTo ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDispatchedTo_MatchingTargetIdAndType_ReturnsOrder()
    {
        var sut = new InMemoryServiceOrderRepository();
        var order = Order("a").DispatchTo("user-1", DispatchTargetType.User, "supervisor-1");
        await sut.AddAsync(order);

        (await sut.GetDispatchedToAsync("user-1", DispatchTargetType.User))
            .Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task GetDispatchedTo_MismatchedTargetId_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a").DispatchTo("user-1", DispatchTargetType.User, "supervisor-1"));

        (await sut.GetDispatchedToAsync("user-2", DispatchTargetType.User)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetDispatchedTo_MismatchedTargetType_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a").DispatchTo("crew-1", DispatchTargetType.Group, "supervisor-1"));

        (await sut.GetDispatchedToAsync("crew-1", DispatchTargetType.User)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetDispatchedTo_NoDispatches_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));

        (await sut.GetDispatchedToAsync("user-1", DispatchTargetType.User)).Should().BeEmpty();
    }

    // ── Add ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_NewOrder_IsStoredAndRetrievable()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));

        (await sut.GetByIdAsync("a")).Should().NotBeNull();
    }

    [Fact]
    public async Task Add_FiresOrderAdded()
    {
        var sut = new InMemoryServiceOrderRepository();
        IServiceOrder? raised = null;
        sut.OrderAdded += (_, o) => raised = o;

        await sut.AddAsync(Order("a"));

        raised!.Id.Should().Be("a");
    }

    [Fact]
    public async Task AddAsync_NonServiceOrderImplementation_ThrowsArgumentException()
    {
        var sut = new InMemoryServiceOrderRepository();

        var act = () => sut.AddAsync(new FakeServiceOrder { Id = "a" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_IllegalStatusTransition_ThrowsInvalidServiceOrderTransitionException()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        var act = () => sut.UpdateAsync(Order("a", status: ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public async Task Update_IllegalStatusTransition_DoesNotMutateStoredOrder()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft, createdBy: "alice"));

        var incoming = Order("a", status: ServiceOrderStatus.Completed, createdBy: "alice");
        incoming.Title = "Should not persist";
        try { await sut.UpdateAsync(incoming); } catch (InvalidServiceOrderTransitionException) { }

        var stored = await sut.GetByIdAsync("a");
        stored!.Status.Should().Be(ServiceOrderStatus.Draft);
        stored.Title.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_ExistingOrderStatusChanged_FiresOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        (IServiceOrder Order, string Previous)? raised = null;
        sut.OrderStatusChanged += (_, e) => raised = e;

        await sut.UpdateAsync(Order("a", status: ServiceOrderStatus.Pending));

        raised.Should().NotBeNull();
        raised!.Value.Previous.Should().Be(ServiceOrderStatus.Draft);
        raised.Value.Order.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task Update_ExistingOrderSameStatus_DoesNotFireOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        var fired = false;
        sut.OrderStatusChanged += (_, _) => fired = true;

        await sut.UpdateAsync(Order("a", status: ServiceOrderStatus.Draft));

        fired.Should().BeFalse();
    }

    [Fact]
    public async Task Update_UnknownOrder_ThrowsKeyNotFoundException()
    {
        var sut = new InMemoryServiceOrderRepository();

        var act = () => sut.UpdateAsync(Order("new", status: ServiceOrderStatus.InProgress));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_NonServiceOrderIncomingArgument_ThrowsArgumentException()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));

        var act = () => sut.UpdateAsync(new FakeServiceOrder { Id = "a" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Update_CopiesAttributes()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));

        var incoming = Order("a");
        incoming.Attributes["zone"] = "north";
        await sut.UpdateAsync(incoming);

        (await sut.GetByIdAsync("a"))!.Attributes.Should().ContainKey("zone").WhoseValue.Should().Be("north");
    }

    [Fact]
    public async Task Update_DoesNotPersistDispatchesOrActionLog()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));

        var incoming = Order("a").DispatchTo("user-1", DispatchTargetType.User, "supervisor-1");
        await sut.UpdateAsync(incoming);

        var stored = await sut.GetByIdAsync("a");
        stored!.Dispatches.Should().BeEmpty();
        stored.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_AlwaysFiresOrderUpdated()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));
        var fired = false;
        sut.OrderUpdated += (_, _) => fired = true;

        await sut.UpdateAsync(Order("a"));

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task Update_StatusChangedWithNoSubscriber_DoesNotThrow()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        var act = () => sut.UpdateAsync(Order("a", status: ServiceOrderStatus.Pending));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Update_ReplacesStoredInstance()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(new ServiceOrder { Id = "a", Title = "Old" });

        await sut.UpdateAsync(new ServiceOrder { Id = "a", Title = "New" });

        (await sut.GetByIdAsync("a"))!.Title.Should().Be("New");
    }

    // ── AppendDispatch ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AppendDispatch_ExistingOrder_AddsDispatch()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));
        var dispatch = new OrderDispatch("user-1", DispatchTargetType.User, "supervisor-1", DateTime.UtcNow);

        await sut.AppendDispatchAsync("a", dispatch);

        (await sut.GetByIdAsync("a"))!.Dispatches.Should().ContainSingle().Which.Should().Be(dispatch);
    }

    [Fact]
    public async Task AppendDispatch_ExistingOrder_FiresOrderUpdated()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));
        var fired = false;
        sut.OrderUpdated += (_, _) => fired = true;

        await sut.AppendDispatchAsync("a", new OrderDispatch("user-1", DispatchTargetType.User, "supervisor-1", DateTime.UtcNow));

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task AppendDispatch_UnknownOrder_ThrowsKeyNotFoundException()
    {
        var sut = new InMemoryServiceOrderRepository();

        var act = () => sut.AppendDispatchAsync("missing", new OrderDispatch("user-1", DispatchTargetType.User, "supervisor-1", DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── AppendAction ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AppendAction_WithoutResultingStatus_AddsEntryAndDoesNotFireOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        var fired = false;
        sut.OrderStatusChanged += (_, _) => fired = true;

        await sut.AppendActionAsync("a", new OrderActionLog(OrderActionType.Annotate, "tech-1", DateTime.UtcNow, "note"));

        var stored = await sut.GetByIdAsync("a");
        stored!.ActionLog.Should().ContainSingle();
        stored.Status.Should().Be(ServiceOrderStatus.Draft);
        fired.Should().BeFalse();
    }

    [Fact]
    public async Task AppendAction_ExistingOrder_FiresOrderUpdated()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));
        var fired = false;
        sut.OrderUpdated += (_, _) => fired = true;

        await sut.AppendActionAsync("a", new OrderActionLog(OrderActionType.Annotate, "tech-1", DateTime.UtcNow));

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task AppendAction_IllegalResultingStatus_ThrowsInvalidServiceOrderTransitionException()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        var act = () => sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Complete, "tech-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public async Task AppendAction_IllegalResultingStatus_DoesNotPersistLogEntry()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));

        try
        {
            await sut.AppendActionAsync("a",
                new OrderActionLog(OrderActionType.Complete, "tech-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Completed));
        }
        catch (InvalidServiceOrderTransitionException) { }

        var stored = await sut.GetByIdAsync("a");
        stored!.ActionLog.Should().BeEmpty();
        stored.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task AppendAction_ResultingStatusEqualToCurrent_DoesNotFireOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        var fired = false;
        sut.OrderStatusChanged += (_, _) => fired = true;

        await sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Annotate, "tech-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Draft));

        fired.Should().BeFalse();
    }

    [Fact]
    public async Task AppendAction_ResultingStatusDifferentFromCurrent_UpdatesStatusAndFiresOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.Draft));
        (IServiceOrder Order, string Previous)? raised = null;
        sut.OrderStatusChanged += (_, e) => raised = e;

        await sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Approve, "supervisor-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Pending));

        raised.Should().NotBeNull();
        raised!.Value.Previous.Should().Be(ServiceOrderStatus.Draft);
        raised.Value.Order.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task AppendAction_ResultingStatusCompleted_SetsCompletedAt()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a", status: ServiceOrderStatus.InProgress));
        var performedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        await sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Complete, "tech-1", performedAt, ResultingStatus: ServiceOrderStatus.Completed));

        (await sut.GetByIdAsync("a"))!.CompletedAt.Should().Be(performedAt);
    }

    [Fact]
    public async Task AppendAction_UnknownOrder_ThrowsKeyNotFoundException()
    {
        var sut = new InMemoryServiceOrderRepository();

        var act = () => sut.AppendActionAsync("missing", new OrderActionLog(OrderActionType.Annotate, "tech-1", DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingId_RemovesOrder()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));

        await sut.DeleteAsync("a");

        (await sut.GetByIdAsync("a")).Should().BeNull();
    }

    [Fact]
    public async Task Delete_ExistingId_FiresOrderDeleted()
    {
        var sut = new InMemoryServiceOrderRepository();
        await sut.AddAsync(Order("a"));
        string? deletedId = null;
        sut.OrderDeleted += (_, id) => deletedId = id;

        await sut.DeleteAsync("a");

        deletedId.Should().Be("a");
    }

    [Fact]
    public async Task Delete_NonExistingId_StillFiresOrderDeleted()
    {
        var sut = new InMemoryServiceOrderRepository();
        string? deletedId = null;
        sut.OrderDeleted += (_, id) => deletedId = id;

        await sut.DeleteAsync("missing");

        deletedId.Should().Be("missing");
    }
}
