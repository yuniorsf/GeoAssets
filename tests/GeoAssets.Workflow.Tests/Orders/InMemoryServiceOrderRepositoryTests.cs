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
        ServiceOrderStatus status = ServiceOrderStatus.Draft,
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
        public ServiceOrderStatus Status { get; init; } = ServiceOrderStatus.Draft;
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
    public void GetById_MissingId_ReturnsNull()
    {
        new InMemoryServiceOrderRepository().GetById("x").Should().BeNull();
    }

    [Fact]
    public void GetById_ExistingOrder_ReturnsOrder()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a"));
        sut.GetById("a")!.Id.Should().Be("a");
    }

    [Fact]
    public void GetById_OrderWithChildren_RecomputesChildOrderIds()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("parent"));
        sut.Add(Order("child-a", parentOrderId: "parent"));
        sut.Add(Order("child-b", parentOrderId: "parent"));

        sut.GetById("parent")!.ChildOrderIds.Should().BeEquivalentTo(["child-a", "child-b"]);
    }

    [Fact]
    public void GetById_NonServiceOrderImplementation_LeavesChildOrderIdsUnchanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        var fake = new FakeServiceOrder { Id = "fake", ChildOrderIds = ["preset"] };
        sut.Add(fake);
        sut.Add(Order("child", parentOrderId: "fake"));

        sut.GetById("fake")!.ChildOrderIds.Should().BeEquivalentTo(["preset"]);
    }

    // ── GetAll ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_EmptyStore_ReturnsEmpty()
    {
        new InMemoryServiceOrderRepository().GetAll().Should().BeEmpty();
    }

    [Fact]
    public void GetAll_WithOrders_ReturnsAllMaterialized()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("parent"));
        sut.Add(Order("child", parentOrderId: "parent"));

        var all = sut.GetAll();

        all.Select(o => o.Id).Should().BeEquivalentTo(["parent", "child"]);
        all.Single(o => o.Id == "parent").ChildOrderIds.Should().BeEquivalentTo(["child"]);
    }

    // ── GetRoots ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetRoots_MixOfRootAndChildOrders_ReturnsOnlyRoots()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("root"));
        sut.Add(Order("child", parentOrderId: "root"));

        sut.GetRoots().Select(o => o.Id).Should().BeEquivalentTo(["root"]);
    }

    // ── GetChildren ────────────────────────────────────────────────────────────

    [Fact]
    public void GetChildren_ParentWithChildren_ReturnsChildren()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("parent"));
        sut.Add(Order("child-a", parentOrderId: "parent"));
        sut.Add(Order("other-root"));

        sut.GetChildren("parent").Select(o => o.Id).Should().BeEquivalentTo(["child-a"]);
    }

    [Fact]
    public void GetChildren_ParentWithNoChildren_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("parent"));

        sut.GetChildren("parent").Should().BeEmpty();
    }

    // ── GetParent ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetParent_ChildIdNotFound_ReturnsNull()
    {
        new InMemoryServiceOrderRepository().GetParent("missing").Should().BeNull();
    }

    [Fact]
    public void GetParent_RootOrder_ReturnsNull()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("root"));

        sut.GetParent("root").Should().BeNull();
    }

    [Fact]
    public void GetParent_ChildWithExistingParent_ReturnsParent()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("parent"));
        sut.Add(Order("child", parentOrderId: "parent"));

        sut.GetParent("child")!.Id.Should().Be("parent");
    }

    [Fact]
    public void GetParent_ChildWithMissingParentRecord_ReturnsNull()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("orphan", parentOrderId: "does-not-exist"));

        sut.GetParent("orphan").Should().BeNull();
    }

    // ── GetByStatus ────────────────────────────────────────────────────────────

    [Fact]
    public void GetByStatus_MatchingOrders_ReturnsThem()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", status: ServiceOrderStatus.Pending));
        sut.Add(Order("b", status: ServiceOrderStatus.Draft));

        sut.GetByStatus(ServiceOrderStatus.Pending).Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void GetByStatus_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", status: ServiceOrderStatus.Draft));

        sut.GetByStatus(ServiceOrderStatus.Completed).Should().BeEmpty();
    }

    // ── GetByAssignee ──────────────────────────────────────────────────────────

    [Fact]
    public void GetByAssignee_MatchingOrder_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", assignedTo: "tech-1"));
        sut.Add(Order("b", assignedTo: "tech-2"));

        sut.GetByAssignee("tech-1").Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void GetByAssignee_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", assignedTo: "tech-1"));

        sut.GetByAssignee("tech-9").Should().BeEmpty();
    }

    // ── GetByCreator ───────────────────────────────────────────────────────────

    [Fact]
    public void GetByCreator_MatchingOrder_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", createdBy: "alice"));
        sut.Add(Order("b", createdBy: "bob"));

        sut.GetByCreator("alice").Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void GetByCreator_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", createdBy: "alice"));

        sut.GetByCreator("carol").Should().BeEmpty();
    }

    // ── GetByOrderType ─────────────────────────────────────────────────────────

    [Fact]
    public void GetByOrderType_MatchingOrder_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", orderTypeId: "inspection"));
        sut.Add(Order("b", orderTypeId: "maintenance"));

        sut.GetByOrderType("inspection").Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void GetByOrderType_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", orderTypeId: "inspection"));

        sut.GetByOrderType("emergency-repair").Should().BeEmpty();
    }

    // ── GetByDateRange ─────────────────────────────────────────────────────────

    [Fact]
    public void GetByDateRange_OrderWithinRange_ReturnsIt()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", createdAt: new DateTime(2026, 1, 15)));

        sut.GetByDateRange(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))
            .Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void GetByDateRange_OrderBeforeRange_Excluded()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", createdAt: new DateTime(2025, 12, 31)));

        sut.GetByDateRange(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)).Should().BeEmpty();
    }

    [Fact]
    public void GetByDateRange_OrderAfterRange_Excluded()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", createdAt: new DateTime(2026, 2, 1)));

        sut.GetByDateRange(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)).Should().BeEmpty();
    }

    // ── GetDispatchedTo ────────────────────────────────────────────────────────

    [Fact]
    public void GetDispatchedTo_MatchingTargetIdAndType_ReturnsOrder()
    {
        var sut = new InMemoryServiceOrderRepository();
        var order = Order("a").DispatchTo("user-1", DispatchTargetType.User, "supervisor-1");
        sut.Add(order);

        sut.GetDispatchedTo("user-1", DispatchTargetType.User)
            .Select(o => o.Id).Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void GetDispatchedTo_MismatchedTargetId_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a").DispatchTo("user-1", DispatchTargetType.User, "supervisor-1"));

        sut.GetDispatchedTo("user-2", DispatchTargetType.User).Should().BeEmpty();
    }

    [Fact]
    public void GetDispatchedTo_MismatchedTargetType_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a").DispatchTo("crew-1", DispatchTargetType.Group, "supervisor-1"));

        sut.GetDispatchedTo("crew-1", DispatchTargetType.User).Should().BeEmpty();
    }

    [Fact]
    public void GetDispatchedTo_NoDispatches_ReturnsEmpty()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a"));

        sut.GetDispatchedTo("user-1", DispatchTargetType.User).Should().BeEmpty();
    }

    // ── Add ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_NewOrder_IsStoredAndRetrievable()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a"));

        sut.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_FiresOrderAdded()
    {
        var sut = new InMemoryServiceOrderRepository();
        IServiceOrder? raised = null;
        sut.OrderAdded += (_, o) => raised = o;

        sut.Add(Order("a"));

        raised!.Id.Should().Be("a");
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ExistingOrderStatusChanged_FiresOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", status: ServiceOrderStatus.Draft));
        (IServiceOrder Order, ServiceOrderStatus Previous)? raised = null;
        sut.OrderStatusChanged += (_, e) => raised = e;

        sut.Update(Order("a", status: ServiceOrderStatus.Pending));

        raised.Should().NotBeNull();
        raised!.Value.Previous.Should().Be(ServiceOrderStatus.Draft);
        raised.Value.Order.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public void Update_ExistingOrderSameStatus_DoesNotFireOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", status: ServiceOrderStatus.Draft));
        var fired = false;
        sut.OrderStatusChanged += (_, _) => fired = true;

        sut.Update(Order("a", status: ServiceOrderStatus.Draft));

        fired.Should().BeFalse();
    }

    [Fact]
    public void Update_UnknownOrder_AddsItWithoutFiringOrderStatusChanged()
    {
        var sut = new InMemoryServiceOrderRepository();
        var fired = false;
        sut.OrderStatusChanged += (_, _) => fired = true;

        sut.Update(Order("new", status: ServiceOrderStatus.InProgress));

        fired.Should().BeFalse();
        sut.GetById("new").Should().NotBeNull();
    }

    [Fact]
    public void Update_AlwaysFiresOrderUpdated()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a"));
        var fired = false;
        sut.OrderUpdated += (_, _) => fired = true;

        sut.Update(Order("a"));

        fired.Should().BeTrue();
    }

    [Fact]
    public void Update_StatusChangedWithNoSubscriber_DoesNotThrow()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a", status: ServiceOrderStatus.Draft));

        var act = () => sut.Update(Order("a", status: ServiceOrderStatus.Pending));

        act.Should().NotThrow();
    }

    [Fact]
    public void Update_ReplacesStoredInstance()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(new ServiceOrder { Id = "a", Title = "Old" });

        sut.Update(new ServiceOrder { Id = "a", Title = "New" });

        sut.GetById("a")!.Title.Should().Be("New");
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingId_RemovesOrder()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a"));

        sut.Delete("a");

        sut.GetById("a").Should().BeNull();
    }

    [Fact]
    public void Delete_ExistingId_FiresOrderDeleted()
    {
        var sut = new InMemoryServiceOrderRepository();
        sut.Add(Order("a"));
        string? deletedId = null;
        sut.OrderDeleted += (_, id) => deletedId = id;

        sut.Delete("a");

        deletedId.Should().Be("a");
    }

    [Fact]
    public void Delete_NonExistingId_StillFiresOrderDeleted()
    {
        var sut = new InMemoryServiceOrderRepository();
        string? deletedId = null;
        sut.OrderDeleted += (_, id) => deletedId = id;

        sut.Delete("missing");

        deletedId.Should().Be("missing");
    }
}
