namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Persistence and query abstraction for <see cref="IServiceOrder"/>.
/// Swap implementations (in-memory, EF Core, remote API) via DI.
/// </summary>
public interface IServiceOrderRepository
{
    // ── Read ──────────────────────────────────────────────────────────────────

    Task<IServiceOrder?>               GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default);
    Task<IServiceOrder?>               GetParentAsync(string childId, CancellationToken ct = default);

    // ── Filtered queries ──────────────────────────────────────────────────────

    Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Returns all orders dispatched to the given target
    /// (user ID, group ID, or organization ID depending on <paramref name="targetType"/>).
    /// </summary>
    Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default);

    // ── Write ─────────────────────────────────────────────────────────────────

    Task AddAsync(IServiceOrder order, CancellationToken ct = default);

    /// <summary>
    /// Persists scalar order fields (title, status, priority, assignee, schedule,
    /// attributes, features, hierarchy) from <paramref name="order"/>.
    /// Does <b>not</b> persist <see cref="IServiceOrder.Dispatches"/> or
    /// <see cref="IServiceOrder.ActionLog"/> — use <see cref="AppendDispatchAsync"/>
    /// and <see cref="AppendActionAsync"/> for those, so concurrent appends from
    /// different callers never race to infer "what's new" from list state.
    /// Throws <see cref="KeyNotFoundException"/> if no order with this ID was
    /// previously added.
    /// </summary>
    Task UpdateAsync(IServiceOrder order, CancellationToken ct = default);

    /// <summary>
    /// Appends a single dispatch event to the order, independent of any other
    /// concurrent write. Throws <see cref="KeyNotFoundException"/> if the order
    /// does not exist.
    /// </summary>
    Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default);

    /// <summary>
    /// Appends a single audit-log entry to the order, independent of any other
    /// concurrent write. When <paramref name="entry"/> carries a
    /// <see cref="OrderActionLog.ResultingStatus"/>, the order's status (and
    /// <see cref="IServiceOrder.CompletedAt"/>, when transitioning to
    /// <see cref="ServiceOrderStatus.Completed"/>) is updated atomically with the
    /// log entry. Throws <see cref="KeyNotFoundException"/> if the order does not exist.
    /// </summary>
    Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    // ── Events ────────────────────────────────────────────────────────────────

    event EventHandler<IServiceOrder>? OrderAdded;
    event EventHandler<IServiceOrder>? OrderUpdated;
    event EventHandler<(IServiceOrder Order, ServiceOrderStatus Previous)>? OrderStatusChanged;
    event EventHandler<string>?        OrderDeleted;
}
