namespace GeoAssets.Workflow.Orders;

/// <summary>Thread-safe in-memory implementation of <see cref="IServiceOrderRepository"/>.</summary>
public sealed class InMemoryServiceOrderRepository : IServiceOrderRepository
{
    private readonly Dictionary<string, IServiceOrder> _store = [];
    private readonly Lock _lock = new();

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<IServiceOrder>?                                       OrderAdded;
    public event EventHandler<IServiceOrder>?                                       OrderUpdated;
    public event EventHandler<(IServiceOrder Order, string Previous)>?             OrderStatusChanged;
    public event EventHandler<string>?                                              OrderDeleted;

    // ── Read ──────────────────────────────────────────────────────────────────

    public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.GetValueOrDefault(id))); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values)); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values.Where(o => o.IsRoot))); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values.Where(o => o.ParentOrderId == parentId))); }
    }

    public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var child = _store.GetValueOrDefault(childId);
            return Task.FromResult(child?.ParentOrderId is { } pid ? Materialize(_store.GetValueOrDefault(pid)) : null);
        }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values.Where(o => o.Status == status))); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values.Where(o => o.AssignedTo == userId))); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values.Where(o => o.CreatedBy == userId))); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values.Where(o => o.OrderTypeId == orderTypeId))); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(Materialize(_store.Values.Where(o => o.CreatedAt >= from && o.CreatedAt <= to))); }
    }

    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(Materialize(_store.Values.Where(o =>
                o.Dispatches.Any(d => d.TargetId == targetId && d.TargetType == targetType))));
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
    /// Every entry in <see cref="_store"/> is guaranteed to be a <see cref="ServiceOrder"/>
    /// by <see cref="AddAsync"/>'s type check — it's the only method that inserts new keys.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private IServiceOrder? Materialize(IServiceOrder? order)
    {
        if (order is null) return null;

        var so = (ServiceOrder)order;
        so.ChildOrderIds.Clear();
        so.ChildOrderIds.AddRange(
            _store.Values.Where(o => o.ParentOrderId == so.Id).Select(o => o.Id));
        return so;
    }

    private IReadOnlyList<IServiceOrder> Materialize(IEnumerable<IServiceOrder> orders)
        => [.. orders.Select(o => Materialize(o)!)];

    /// <summary>
    /// Looks up <paramref name="id"/> and returns it as a concrete <see cref="ServiceOrder"/>
    /// so its mutable collections can be updated in place. See <see cref="Materialize(IServiceOrder?)"/>
    /// for why the cast is guaranteed safe.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private ServiceOrder RequireConcrete(string id)
    {
        if (!_store.TryGetValue(id, out var order))
            throw new KeyNotFoundException($"ServiceOrder '{id}' not found.");

        return (ServiceOrder)order;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
    {
        if (order is not ServiceOrder)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        lock (_lock) { _store[order.Id] = order; }
        OrderAdded?.Invoke(this, order);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        if (order is not ServiceOrder incoming)
            throw new ArgumentException($"Expected {nameof(ServiceOrder)}.", nameof(order));

        ServiceOrder existing;
        string previous;
        lock (_lock)
        {
            existing = RequireConcrete(order.Id);
            previous = existing.Status;

            if (!ServiceOrderTransitions.IsValid(previous, incoming.Status))
                throw new InvalidServiceOrderTransitionException(previous, incoming.Status);

            existing.Title         = incoming.Title;
            existing.Description   = incoming.Description;
            existing.OrderTypeId   = incoming.OrderTypeId;
            existing.Status        = incoming.Status;
            existing.Priority      = incoming.Priority;
            existing.AssignedTo    = incoming.AssignedTo;
            existing.UpdatedAt     = incoming.UpdatedAt;
            existing.ScheduledAt   = incoming.ScheduledAt;
            existing.CompletedAt   = incoming.CompletedAt;
            existing.ParentOrderId = incoming.ParentOrderId;
            existing.SelectionSpec = incoming.SelectionSpec;
            existing.FeatureIds    = incoming.FeatureIds;

            existing.Attributes.Clear();
            foreach (var kv in incoming.Attributes) existing.Attributes[kv.Key] = kv.Value;

            existing.Features.Clear();
            existing.Features.AddRange(incoming.Features);
        }

        OrderUpdated?.Invoke(this, existing);

        if (previous != existing.Status)
            OrderStatusChanged?.Invoke(this, (existing, previous));

        return Task.CompletedTask;
    }

    public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
    {
        ServiceOrder order;
        lock (_lock)
        {
            order = RequireConcrete(orderId);
            order.Dispatches.Add(dispatch);
            order.UpdatedAt = dispatch.DispatchedAt;
        }

        OrderUpdated?.Invoke(this, order);
        return Task.CompletedTask;
    }

    public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        ServiceOrder order;
        string previous;
        lock (_lock)
        {
            order = RequireConcrete(orderId);
            previous = order.Status;

            if (entry.ResultingStatus is not null && !ServiceOrderTransitions.IsValid(previous, entry.ResultingStatus))
                throw new InvalidServiceOrderTransitionException(previous, entry.ResultingStatus);

            order.ActionLog.Add(entry);

            if (entry.ResultingStatus is not null)
            {
                order.Status    = entry.ResultingStatus;
                order.UpdatedAt = entry.PerformedAt;
                if (ServiceOrderTransitions.IsSuccessState(null, entry.ResultingStatus))
                    order.CompletedAt = entry.PerformedAt;
            }
            else
            {
                order.UpdatedAt = entry.PerformedAt;
            }
        }

        OrderUpdated?.Invoke(this, order);

        if (entry.ResultingStatus is not null && entry.ResultingStatus != previous)
            OrderStatusChanged?.Invoke(this, (order, previous));

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) { _store.Remove(id); }
        OrderDeleted?.Invoke(this, id);
        return Task.CompletedTask;
    }
}
