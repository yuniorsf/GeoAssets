namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Decorates any <see cref="IServiceOrderRepository"/> implementation with
/// <see cref="ServiceOrderTransitions"/> enforcement on every status-changing write
/// (<see cref="UpdateAsync"/>, <see cref="AppendActionAsync"/>), so a new implementation
/// gets this guarantee automatically instead of having to reimplement the check itself.
///
/// <c>EFServiceOrderRepository</c> already validates directly and remains perfectly safe to
/// use unwrapped — this decorator adds the same guarantee for any repository that doesn't,
/// without requiring every implementer to remember to add it. <c>AddWorkflowPersistence</c>
/// registers it by default.
/// </summary>
public sealed class ValidatingServiceOrderRepository(
    IServiceOrderRepository inner,
    OrderTypeRegistry? orderTypeRegistry = null) : IServiceOrderRepository
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

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default)
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
    {
        ValidateAttributes(order);
        return inner.AddAsync(order, ct);
    }

    public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        ValidateAttributes(order);

        var existing = await inner.GetByIdAsync(order.Id, ct)
            ?? throw new KeyNotFoundException($"ServiceOrder '{order.Id}' not found.");

        var orderType = orderTypeRegistry?.Find(order.OrderTypeId);
        if (!ServiceOrderTransitions.IsValid(orderType, existing.Status, order.Status))
            throw new InvalidServiceOrderTransitionException(existing.Status, order.Status);

        await inner.UpdateAsync(order, ct);
    }

    /// <summary>
    /// No-op when no <see cref="OrderTypeRegistry"/> was supplied, or the order's type isn't
    /// registered, or the registered type has no <see cref="OrderType.AttributesSchemaJson"/> —
    /// same "unrestricted by default" behavior as <see cref="ServiceOrderRules"/>'s optional
    /// registry parameter.
    /// </summary>
    private void ValidateAttributes(IServiceOrder order)
    {
        var orderType = orderTypeRegistry?.Find(order.OrderTypeId);
        if (orderType is not null)
            ServiceOrderAttributeValidator.EnsureValid(orderType, order.Attributes);
    }

    public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
        => inner.AppendDispatchAsync(orderId, dispatch, ct);

    public async Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        if (entry.ResultingStatus is { } resultingStatus)
        {
            var existing = await inner.GetByIdAsync(orderId, ct)
                ?? throw new KeyNotFoundException($"ServiceOrder '{orderId}' not found.");

            var orderType = orderTypeRegistry?.Find(existing.OrderTypeId);
            if (!ServiceOrderTransitions.IsValid(orderType, existing.Status, resultingStatus))
                throw new InvalidServiceOrderTransitionException(existing.Status, resultingStatus);
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

    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged
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
