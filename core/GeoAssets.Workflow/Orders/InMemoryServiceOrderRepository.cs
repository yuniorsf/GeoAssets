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
        lock (_lock) { return Materialize(_store.GetValueOrDefault(id)); }
    }

    public IReadOnlyList<IServiceOrder> GetAll()
    {
        lock (_lock) { return Materialize(_store.Values); }
    }

    public IReadOnlyList<IServiceOrder> GetRoots()
    {
        lock (_lock) { return Materialize(_store.Values.Where(o => o.IsRoot)); }
    }

    public IReadOnlyList<IServiceOrder> GetChildren(string parentId)
    {
        lock (_lock) { return Materialize(_store.Values.Where(o => o.ParentOrderId == parentId)); }
    }

    public IServiceOrder? GetParent(string childId)
    {
        lock (_lock)
        {
            var child = _store.GetValueOrDefault(childId);
            return child?.ParentOrderId is { } pid ? Materialize(_store.GetValueOrDefault(pid)) : null;
        }
    }

    public IReadOnlyList<IServiceOrder> GetByStatus(ServiceOrderStatus status)
    {
        lock (_lock) { return Materialize(_store.Values.Where(o => o.Status == status)); }
    }

    public IReadOnlyList<IServiceOrder> GetByAssignee(string userId)
    {
        lock (_lock) { return Materialize(_store.Values.Where(o => o.AssignedTo == userId)); }
    }

    public IReadOnlyList<IServiceOrder> GetByCreator(string userId)
    {
        lock (_lock) { return Materialize(_store.Values.Where(o => o.CreatedBy == userId)); }
    }

    public IReadOnlyList<IServiceOrder> GetByOrderType(string orderTypeId)
    {
        lock (_lock) { return Materialize(_store.Values.Where(o => o.OrderTypeId == orderTypeId)); }
    }

    public IReadOnlyList<IServiceOrder> GetByDateRange(DateTime from, DateTime to)
    {
        lock (_lock) { return Materialize(_store.Values.Where(o => o.CreatedAt >= from && o.CreatedAt <= to)); }
    }

    public IReadOnlyList<IServiceOrder> GetDispatchedTo(string targetId, DispatchTargetType targetType)
    {
        lock (_lock)
        {
            return Materialize(_store.Values.Where(o =>
                o.Dispatches.Any(d => d.TargetId == targetId && d.TargetType == targetType)));
        }
    }

    // ── Materialization ───────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes <see cref="IServiceOrder.ChildOrderIds"/> from the current
    /// <see cref="IServiceOrder.ParentOrderId"/> links in the store, mirroring
    /// how <c>EFServiceOrderRepository</c> derives children on every read.
    /// <see cref="ParentOrderId"/> is the only persisted source of truth for
    /// hierarchy; <c>ChildOrderIds</c> is a derived convenience view and any
    /// manual mutation of it is overwritten the next time the order is read.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private IServiceOrder? Materialize(IServiceOrder? order)
    {
        if (order is ServiceOrder so)
        {
            so.ChildOrderIds.Clear();
            so.ChildOrderIds.AddRange(
                _store.Values.Where(o => o.ParentOrderId == so.Id).Select(o => o.Id));
        }
        return order;
    }

    private IReadOnlyList<IServiceOrder> Materialize(IEnumerable<IServiceOrder> orders)
        => [.. orders.Select(o => Materialize(o)!)];

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
