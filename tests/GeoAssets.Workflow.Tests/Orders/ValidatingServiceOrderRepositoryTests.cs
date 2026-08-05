using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class ValidatingServiceOrderRepositoryTests
{
    private static ServiceOrder Order(string id, string status = ServiceOrderStatus.Draft)
        => new() { Id = id, Status = status };

    /// <summary>
    /// Deliberately naive inner repository: stores whatever it's given, with no
    /// transition-legality checks of its own. Used to prove the decorator itself
    /// is what rejects illegal transitions, independent of the inner implementation.
    /// </summary>
    private sealed class NaiveServiceOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, IServiceOrder> _store = [];

        public int GetByIdAsyncCallCount { get; private set; }
        public int UpdateAsyncCallCount { get; private set; }
        public int AppendActionAsyncCallCount { get; private set; }

        public event EventHandler<IServiceOrder>? OrderAdded;
        public event EventHandler<IServiceOrder>? OrderUpdated;
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
        public event EventHandler<string>? OrderDeleted;

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            GetByIdAsyncCallCount++;
            return Task.FromResult(_store.GetValueOrDefault(id));
        }

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => throw new NotImplementedException();

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        {
            _store[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            UpdateAsyncCallCount++;
            _store[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw new NotImplementedException();

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            AppendActionAsyncCallCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    }

    // ── Read / query pass-through ──────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetByIdAsync("a"))!.Id.Should().Be("a");
    }

    [Fact]
    public async Task GetAllAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetRootsAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetRootsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetChildrenAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("parent"));
        await inner.AddAsync(new ServiceOrder { Id = "child", ParentOrderId = "parent" });
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetChildrenAsync("parent")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetParentAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("parent"));
        await inner.AddAsync(new ServiceOrder { Id = "child", ParentOrderId = "parent" });
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetParentAsync("child"))!.Id.Should().Be("parent");
    }

    [Fact]
    public async Task GetByStatusAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Pending));
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetByStatusAsync(ServiceOrderStatus.Pending)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByAssigneeAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", AssignedTo = "tech-1" });
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetByAssigneeAsync("tech-1")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByCreatorAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", CreatedBy = "alice" });
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetByCreatorAsync("alice")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByOrderTypeAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", OrderTypeId = "inspection" });
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetByOrderTypeAsync("inspection")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByDateRangeAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", CreatedAt = new DateTime(2026, 1, 15) });
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetByDateRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31))).Should().ContainSingle();
    }

    [Fact]
    public async Task GetDispatchedToAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a").DispatchTo("user-1", DispatchTargetType.User, "supervisor-1"));
        var sut = new ValidatingServiceOrderRepository(inner);

        (await sut.GetDispatchedToAsync("user-1", DispatchTargetType.User)).Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner);

        await sut.AddAsync(Order("a"));

        (await inner.GetByIdAsync("a")).Should().NotBeNull();
    }

    [Fact]
    public async Task AppendDispatchAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var sut = new ValidatingServiceOrderRepository(inner);

        await sut.AppendDispatchAsync("a", new OrderDispatch("user-1", DispatchTargetType.User, "supervisor-1", DateTime.UtcNow));

        (await inner.GetByIdAsync("a"))!.Dispatches.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var sut = new ValidatingServiceOrderRepository(inner);

        await sut.DeleteAsync("a");

        (await inner.GetByIdAsync("a")).Should().BeNull();
    }

    // ── Events (forwarded, add and remove) ─────────────────────────────────────

    [Fact]
    public async Task OrderAdded_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new InMemoryServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner);
        var count = 0;
        EventHandler<IServiceOrder> handler = (_, _) => count++;

        sut.OrderAdded += handler;
        await sut.AddAsync(Order("a"));
        count.Should().Be(1);

        sut.OrderAdded -= handler;
        await sut.AddAsync(Order("b"));
        count.Should().Be(1);
    }

    [Fact]
    public async Task OrderUpdated_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var sut = new ValidatingServiceOrderRepository(inner);
        var count = 0;
        EventHandler<IServiceOrder> handler = (_, _) => count++;

        sut.OrderUpdated += handler;
        await sut.UpdateAsync(Order("a"));
        count.Should().Be(1);

        sut.OrderUpdated -= handler;
        await sut.UpdateAsync(Order("a"));
        count.Should().Be(1);
    }

    [Fact]
    public async Task OrderStatusChanged_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var sut = new ValidatingServiceOrderRepository(inner);
        var count = 0;
        EventHandler<(IServiceOrder Order, string Previous)> handler = (_, _) => count++;

        sut.OrderStatusChanged += handler;
        await sut.UpdateAsync(Order("a", ServiceOrderStatus.Pending));
        count.Should().Be(1);

        sut.OrderStatusChanged -= handler;
        await sut.UpdateAsync(Order("a", ServiceOrderStatus.InProgress));
        count.Should().Be(1);
    }

    [Fact]
    public async Task OrderDeleted_ForwardsAndCanBeUnsubscribed()
    {
        var inner = new InMemoryServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        await inner.AddAsync(Order("b"));
        var sut = new ValidatingServiceOrderRepository(inner);
        var count = 0;
        EventHandler<string> handler = (_, _) => count++;

        sut.OrderDeleted += handler;
        await sut.DeleteAsync("a");
        count.Should().Be(1);

        sut.OrderDeleted -= handler;
        await sut.DeleteAsync("b");
        count.Should().Be(1);
    }

    // ── UpdateAsync validation ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_LegalTransition_DelegatesToInner()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var sut = new ValidatingServiceOrderRepository(inner);

        await sut.UpdateAsync(Order("a", ServiceOrderStatus.Pending));

        inner.UpdateAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_IllegalTransition_ThrowsAndDoesNotCallInner()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var sut = new ValidatingServiceOrderRepository(inner);

        var act = () => sut.UpdateAsync(Order("a", ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        inner.UpdateAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_UnknownOrder_ThrowsKeyNotFoundException()
    {
        var inner = new NaiveServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner);

        var act = () => sut.UpdateAsync(Order("missing"));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        inner.UpdateAsyncCallCount.Should().Be(0);
    }

    // ── AppendActionAsync validation ───────────────────────────────────────────

    [Fact]
    public async Task AppendActionAsync_NoResultingStatus_DelegatesWithoutFetchingExisting()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(Order("a"));
        var sut = new ValidatingServiceOrderRepository(inner);

        await sut.AppendActionAsync("a", new OrderActionLog(OrderActionType.Annotate, "tech-1", DateTime.UtcNow));

        inner.AppendActionAsyncCallCount.Should().Be(1);
        inner.GetByIdAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AppendActionAsync_LegalResultingStatus_DelegatesToInner()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var sut = new ValidatingServiceOrderRepository(inner);

        await sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Approve, "supervisor-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Pending));

        inner.AppendActionAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AppendActionAsync_IllegalResultingStatus_ThrowsAndDoesNotCallInner()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(Order("a", ServiceOrderStatus.Draft));
        var sut = new ValidatingServiceOrderRepository(inner);

        var act = () => sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Complete, "tech-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        inner.AppendActionAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AppendActionAsync_UnknownOrderWithResultingStatus_ThrowsKeyNotFoundException()
    {
        var inner = new NaiveServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner);

        var act = () => sut.AppendActionAsync("missing",
            new OrderActionLog(OrderActionType.Approve, "supervisor-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Pending));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        inner.AppendActionAsyncCallCount.Should().Be(0);
    }

    // ── Per-OrderType workflow graph validation (XD01-3) ───────────────────────

    private static OrderTypeRegistry RegistryWithCustomGraph()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id          = "custom",
            DisplayName = "Custom",
            States      = [new("Intake", "Intake"), new("Triaged", "Triaged"), new("Closed", "Closed", IsSuccess: true)],
            Transitions = [new("Intake", "Triaged"), new("Triaged", "Closed")],
        });
        return registry;
    }

    private static ServiceOrder CustomOrder(string id, string status) =>
        new() { Id = id, Status = status, OrderTypeId = "custom" };

    [Fact]
    public async Task UpdateAsync_CustomOrderTypeGraph_AllowsEdgeAbsentFromGlobalGraph()
    {
        // "Intake -> Triaged" isn't a state the global graph knows about at all —
        // proving the custom order type's own graph is what's actually consulted.
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(CustomOrder("a", "Intake"));
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithCustomGraph());

        await sut.UpdateAsync(CustomOrder("a", "Triaged"));

        inner.UpdateAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_CustomOrderTypeGraph_RejectsEdgeTheGraphDoesNotDefine()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(CustomOrder("a", "Intake"));
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithCustomGraph());

        // Skips straight to Closed — no such edge in the custom graph.
        var act = () => sut.UpdateAsync(CustomOrder("a", "Closed"));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        inner.UpdateAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AppendActionAsync_CustomOrderTypeGraph_AllowsEdgeAbsentFromGlobalGraph()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(CustomOrder("a", "Intake"));
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithCustomGraph());

        await sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Approve, "triager-1", DateTime.UtcNow, ResultingStatus: "Triaged"));

        inner.AppendActionAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AppendActionAsync_CustomOrderTypeGraph_RejectsEdgeTheGraphDoesNotDefine()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(CustomOrder("a", "Intake"));
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithCustomGraph());

        var act = () => sut.AppendActionAsync("a",
            new OrderActionLog(OrderActionType.Complete, "triager-1", DateTime.UtcNow, ResultingStatus: "Closed"));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        inner.AppendActionAsyncCallCount.Should().Be(0);
    }

    // ── Attribute schema validation (XD01-2) ───────────────────────────────────

    private const string RequiresSeveritySchema = """
    {
      "type": "object",
      "properties": { "severity": { "type": "integer" } },
      "required": ["severity"]
    }
    """;

    private static OrderTypeRegistry RegistryWithSchema()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType
        {
            Id = "emergency-repair",
            DisplayName = "Emergency Repair",
            AttributesSchemaJson = RequiresSeveritySchema,
        });
        return registry;
    }

    [Fact]
    public async Task AddAsync_NoRegistry_SkipsAttributeValidation()
    {
        var inner = new InMemoryServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner);

        var order = new ServiceOrder { Id = "a", OrderTypeId = "emergency-repair" };
        await sut.AddAsync(order);

        (await inner.GetByIdAsync("a")).Should().NotBeNull();
    }

    [Fact]
    public async Task AddAsync_OrderTypeNotRegistered_SkipsAttributeValidation()
    {
        var inner = new InMemoryServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithSchema());

        var order = new ServiceOrder { Id = "a", OrderTypeId = "unregistered-type" };
        await sut.AddAsync(order);

        (await inner.GetByIdAsync("a")).Should().NotBeNull();
    }

    [Fact]
    public async Task AddAsync_ValidAttributes_DelegatesToInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithSchema());

        var order = new ServiceOrder
        {
            Id = "a",
            OrderTypeId = "emergency-repair",
            Attributes = { ["severity"] = "3" },
        };
        await sut.AddAsync(order);

        (await inner.GetByIdAsync("a")).Should().NotBeNull();
    }

    [Fact]
    public async Task AddAsync_InvalidAttributes_ThrowsAndDoesNotCallInner()
    {
        var inner = new InMemoryServiceOrderRepository();
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithSchema());

        var order = new ServiceOrder { Id = "a", OrderTypeId = "emergency-repair" };
        var act = () => sut.AddAsync(order);

        await act.Should().ThrowAsync<ServiceOrderAttributeValidationException>();
        (await inner.GetByIdAsync("a")).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_InvalidAttributes_ThrowsAndDoesNotCallInner()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", OrderTypeId = "emergency-repair" });
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithSchema());

        var act = () => sut.UpdateAsync(new ServiceOrder { Id = "a", OrderTypeId = "emergency-repair" });

        await act.Should().ThrowAsync<ServiceOrderAttributeValidationException>();
        inner.UpdateAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_ValidAttributes_DelegatesToInner()
    {
        var inner = new NaiveServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "a", OrderTypeId = "emergency-repair" });
        var sut = new ValidatingServiceOrderRepository(inner, RegistryWithSchema());

        await sut.UpdateAsync(new ServiceOrder
        {
            Id = "a",
            OrderTypeId = "emergency-repair",
            Attributes = { ["severity"] = "3" },
        });

        inner.UpdateAsyncCallCount.Should().Be(1);
    }
}
