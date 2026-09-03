using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Agents.Tests.TestDoubles;

/// <summary>
/// An <see cref="IServiceOrderRepository"/> double that clones on every write and every read,
/// so callers never share object references with what's actually stored — the same shape a
/// real out-of-process repository (e.g. EF Core) has, unlike
/// <see cref="InMemoryServiceOrderRepository"/> which aliases the exact instance it was given.
/// Use this whenever a test needs to prove something was actually persisted, not merely that
/// an in-memory reference was mutated in place.
///
/// Deliberately does <b>not</b> honor <see cref="IServiceOrderRepository"/>'s correctness
/// contract: <see cref="AppendActionAsync"/> sets <c>Status</c> directly with no
/// <c>ServiceOrderTransitions.IsValid</c> check, and no read method recomputes
/// <c>ChildOrderIds</c> from <c>ParentOrderId</c>. Safe only because
/// <c>DispatchServiceOrderExecutorTests</c> — its one call site — never exercises illegal
/// transitions or hierarchy reads through it. See XD01-27; the shared contract suite that
/// mechanically checks both rules (<c>GeoAssets.Workflow.TestKit.ServiceOrderRepositoryContractTests</c>)
/// intentionally does not run against this type.
/// </summary>
public sealed class SnapshottingServiceOrderRepository : IServiceOrderRepository
{
    private readonly Dictionary<string, ServiceOrder> _store = [];

    private static ServiceOrder Clone(ServiceOrder order) => new()
    {
        Id            = order.Id,
        Title         = order.Title,
        Description   = order.Description,
        OrderTypeId   = order.OrderTypeId,
        Status        = order.Status,
        Priority      = order.Priority,
        CreatedBy     = order.CreatedBy,
        AssignedTo    = order.AssignedTo,
        CreatedAt     = order.CreatedAt,
        UpdatedAt     = order.UpdatedAt,
        ScheduledAt   = order.ScheduledAt,
        CompletedAt   = order.CompletedAt,
        ParentOrderId = order.ParentOrderId,
        Attributes    = new Dictionary<string, string>(order.Attributes),
        Features      = [.. order.Features],
        SelectionSpec = order.SelectionSpec,
        Dispatches    = [.. order.Dispatches],
        ActionLog     = [.. order.ActionLog],
    };

    public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
    {
        _store[order.Id] = Clone((ServiceOrder)order);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(order.Id, out var existing))
            throw new KeyNotFoundException(order.Id);

        var updated = Clone((ServiceOrder)order);
        updated.Dispatches.Clear();
        updated.Dispatches.AddRange(existing.Dispatches);
        updated.ActionLog.Clear();
        updated.ActionLog.AddRange(existing.ActionLog);
        _store[order.Id] = updated;
        return Task.CompletedTask;
    }

    public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
    {
        _store[orderId].Dispatches.Add(dispatch);
        return Task.CompletedTask;
    }

    public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        var order = _store[orderId];
        order.ActionLog.Add(entry);
        if (entry.ResultingStatus is not null)
            order.Status = entry.ResultingStatus;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }

    public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(id, out var order) ? (IServiceOrder)Clone(order) : null);

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Select(Clone)]);

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.ParentOrderId is null).Select(Clone)]);

    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.ParentOrderId == parentId).Select(Clone)]);

    public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(childId, out var child) && child.ParentOrderId is not null
            ? (IServiceOrder?)Clone(_store[child.ParentOrderId])
            : null);

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.Status == status).Select(Clone)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.AssignedTo == userId).Select(Clone)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.CreatedBy == userId).Select(Clone)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.OrderTypeId == orderTypeId).Select(Clone)]);

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _store.Values.Where(o => o.CreatedAt >= from && o.CreatedAt <= to).Select(Clone)]);

    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IServiceOrder>>(
            [.. _store.Values.Where(o => o.Dispatches.Any(d => d.TargetId == targetId && d.TargetType == targetType)).Select(Clone)]);

    public event EventHandler<IServiceOrder>? OrderAdded;
    public event EventHandler<IServiceOrder>? OrderUpdated;
    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
    public event EventHandler<string>? OrderDeleted;
}
