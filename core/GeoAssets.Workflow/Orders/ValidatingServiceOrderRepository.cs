namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Decorates any <see cref="IServiceOrderRepository"/> implementation with
/// <see cref="ServiceOrderTransitions"/> enforcement on every status-changing write
/// (<see cref="UpdateAsync"/>, <see cref="AppendActionAsync"/>), so a new implementation
/// gets this guarantee automatically instead of having to reimplement the check itself.
///
/// <see cref="InMemoryServiceOrderRepository"/> and <c>EFServiceOrderRepository</c>
/// already validate directly and remain perfectly safe to use unwrapped — this decorator
/// adds the same guarantee for any repository that doesn't, without requiring every
/// implementer to remember to add it. <see cref="WorkflowServiceExtensions.AddWorkflowInMemory"/>
/// and <c>AddWorkflowPersistence</c> register it by default.
/// </summary>
public sealed class ValidatingServiceOrderRepository(IServiceOrderRepository inner) : IServiceOrderRepository
{
    // ── Read (pass-through) ────────────────────────────────────────────────────

    public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
        => inner.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default)
        => inner.GetAllAsync(ct);

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default)
        => inner.GetRootsAsync(ct);

    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default)
        => inner.GetChildrenAsync(parentId, ct);

    public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
        => inner.GetParentAsync(childId, ct);

    // ── Filtered queries (pass-through) ────────────────────────────────────────

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default)
        => inner.GetByStatusAsync(status, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default)
        => inner.GetByAssigneeAsync(userId, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default)
        => inner.GetByCreatorAsync(userId, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default)
        => inner.GetByOrderTypeAsync(orderTypeId, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => inner.GetByDateRangeAsync(from, to, ct);

    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default)
        => inner.GetDispatchedToAsync(targetId, targetType, ct);

    // ── Write ──────────────────────────────────────────────────────────────────

    public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        => inner.AddAsync(order, ct);

    public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        var existing = await inner.GetByIdAsync(order.Id, ct)
            ?? throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

        if (!ServiceOrderTransitions.IsValid(existing.Status, order.Status))
            throw new InvalidServiceOrderTransitionException(existing.Status, order.Status);

        await inner.UpdateAsync(order, ct);
    }

    public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
        => inner.AppendDispatchAsync(orderId, dispatch, ct);

    public async Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        if (entry.ResultingStatus.HasValue)
        {
            var existing = await inner.GetByIdAsync(orderId, ct)
                ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

            if (!ServiceOrderTransitions.IsValid(existing.Status, entry.ResultingStatus.Value))
                throw new InvalidServiceOrderTransitionException(existing.Status, entry.ResultingStatus.Value);
        }

        await inner.AppendActionAsync(orderId, entry, ct);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => inner.DeleteAsync(id, ct);

    // ── Events (forwarded) ─────────────────────────────────────────────────────

    public event EventHandler<IServiceOrder>? OrderAdded
    {
        add    => inner.OrderAdded += value;
        remove => inner.OrderAdded -= value;
    }

    public event EventHandler<IServiceOrder>? OrderUpdated
    {
        add    => inner.OrderUpdated += value;
        remove => inner.OrderUpdated -= value;
    }

    public event EventHandler<(IServiceOrder Order, ServiceOrderStatus Previous)>? OrderStatusChanged
    {
        add    => inner.OrderStatusChanged += value;
        remove => inner.OrderStatusChanged -= value;
    }

    public event EventHandler<string>? OrderDeleted
    {
        add    => inner.OrderDeleted += value;
        remove => inner.OrderDeleted -= value;
    }
}
