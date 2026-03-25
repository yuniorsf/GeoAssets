namespace GeoAssets.Workflow.Orders;

/// <summary>Thread-safe in-memory implementation of <see cref="IServiceOrderRepository"/>.</summary>
public sealed class InMemoryServiceOrderRepository : IServiceOrderRepository
{
    private readonly Dictionary<string, IServiceOrder> _store = [];
    private readonly Lock _lock = new();

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<IServiceOrder>?                                       OrderAdded;
    public event EventHandler<IServiceOrder>?                                       OrderUpdated;
    public event EventHandler<(IServiceOrder Order, ServiceOrderStatus Previous)>?  OrderStatusChanged;
    public event EventHandler<string>?                                              OrderDeleted;

    // ── Read ──────────────────────────────────────────────────────────────────

    public IServiceOrder? GetById(string id)
    {
        lock (_lock) { _store.TryGetValue(id, out var o); return o; }
    }

    public IReadOnlyList<IServiceOrder> GetAll()
    {
        lock (_lock) { return [.. _store.Values]; }
    }

    public IReadOnlyList<IServiceOrder> GetRoots()
    {
        lock (_lock) { return [.. _store.Values.Where(o => o.IsRoot)]; }
    }

    public IReadOnlyList<IServiceOrder> GetChildren(string parentId)
    {
        lock (_lock) { return [.. _store.Values.Where(o => o.ParentOrderId == parentId)]; }
    }

    public IServiceOrder? GetParent(string childId)
    {
        lock (_lock)
        {
            var child = GetById(childId);
            return child?.ParentOrderId is { } pid ? GetById(pid) : null;
        }
    }

    public IReadOnlyList<IServiceOrder> GetByStatus(ServiceOrderStatus status)
    {
        lock (_lock) { return [.. _store.Values.Where(o => o.Status == status)]; }
    }

    public IReadOnlyList<IServiceOrder> GetByAssignee(string userId)
    {
        lock (_lock) { return [.. _store.Values.Where(o => o.AssignedTo == userId)]; }
    }

    public IReadOnlyList<IServiceOrder> GetByCreator(string userId)
    {
        lock (_lock) { return [.. _store.Values.Where(o => o.CreatedBy == userId)]; }
    }

    public IReadOnlyList<IServiceOrder> GetByOrderType(string orderTypeId)
    {
        lock (_lock) { return [.. _store.Values.Where(o => o.OrderTypeId == orderTypeId)]; }
    }

    public IReadOnlyList<IServiceOrder> GetByDateRange(DateTime from, DateTime to)
    {
        lock (_lock) { return [.. _store.Values.Where(o => o.CreatedAt >= from && o.CreatedAt <= to)]; }
    }

    public IReadOnlyList<IServiceOrder> GetDispatchedTo(string targetId, DispatchTargetType targetType)
    {
        lock (_lock)
        {
            return [.. _store.Values.Where(o =>
                o.Dispatches.Any(d => d.TargetId == targetId && d.TargetType == targetType))];
        }
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public void Add(IServiceOrder order)
    {
        lock (_lock) { _store[order.Id] = order; }
        OrderAdded?.Invoke(this, order);
    }

    public void Update(IServiceOrder order)
    {
        ServiceOrderStatus? previous = null;
        lock (_lock)
        {
            if (_store.TryGetValue(order.Id, out var existing))
                previous = existing.Status;
            _store[order.Id] = order;
        }

        OrderUpdated?.Invoke(this, order);

        if (previous.HasValue && previous.Value != order.Status)
            OrderStatusChanged?.Invoke(this, (order, previous.Value));
    }

    public void Delete(string id)
    {
        lock (_lock) { _store.Remove(id); }
        OrderDeleted?.Invoke(this, id);
    }
}
