using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Working <see cref="IServiceOrderRepository"/>/<see cref="IOrderTypeRepository"/> test doubles
/// replacing the old <c>AddWorkflowInMemory()</c> DI registration (removed in XD01-129) in test
/// hosts that need a real, mutable store rather than a stub. Deliberately does not validate
/// transitions itself — register it wrapped in <see cref="ValidatingServiceOrderRepository"/>,
/// the same decorator <c>AddWorkflowInMemory()</c> used to apply, if a test needs that guarantee.
/// </summary>
internal sealed class FakeServiceOrderRepository : IServiceOrderRepository
{
    private readonly Dictionary<string, IServiceOrder> _store = [];

    public event EventHandler<IServiceOrder>? OrderAdded;
    public event EventHandler<IServiceOrder>? OrderUpdated;
    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
    public event EventHandler<string>? OrderDeleted;

    public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values]);

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.IsRoot)]);

    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.ParentOrderId == parentId)]);

    public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
    {
        var child = _store.GetValueOrDefault(childId);
        return Task.FromResult(child?.ParentOrderId is { } pid ? _store.GetValueOrDefault(pid) : null);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.Status == status)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.AssignedTo == userId)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.CreatedBy == userId)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.OrderTypeId == orderTypeId)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.CreatedAt >= from && o.CreatedAt <= to)]);

    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>(
            [.. _store.Values.Where(o => o.Dispatches.Any(d => d.TargetId == targetId && d.TargetType == targetType))]);

    public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
    {
        _store[order.Id] = order;
        OrderAdded?.Invoke(this, order);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(order.Id, out var existing))
            throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

        var previous = existing.Status;
        _store[order.Id] = order;

        OrderUpdated?.Invoke(this, order);
        if (previous != order.Status)
            OrderStatusChanged?.Invoke(this, (order, previous));

        return Task.CompletedTask;
    }

    public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
    {
        var order = (ServiceOrder)_store[orderId];
        order.Dispatches.Add(dispatch);
        OrderUpdated?.Invoke(this, order);
        return Task.CompletedTask;
    }

    public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        var order = (ServiceOrder)_store[orderId];
        var previous = order.Status;
        order.ActionLog.Add(entry);
        if (entry.ResultingStatus is not null)
            order.Status = entry.ResultingStatus;

        OrderUpdated?.Invoke(this, order);
        if (entry.ResultingStatus is not null && entry.ResultingStatus != previous)
            OrderStatusChanged?.Invoke(this, (order, previous));

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _store.Remove(id);
        OrderDeleted?.Invoke(this, id);
        return Task.CompletedTask;
    }
}

/// <summary>Working <see cref="IOrderTypeRepository"/> test double — see <see cref="FakeServiceOrderRepository"/>.</summary>
internal sealed class FakeOrderTypeRepository : IOrderTypeRepository
{
    private readonly Dictionary<string, OrderType> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task<OrderType?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<OrderType>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderType>>([.. _store.Values]);

    public Task AddAsync(OrderType orderType, CancellationToken ct = default)
    {
        _store[orderType.Id] = orderType;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OrderType orderType, CancellationToken ct = default)
    {
        _store[orderType.Id] = orderType;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
