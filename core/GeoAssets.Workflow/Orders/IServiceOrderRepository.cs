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
    Task UpdateAsync(IServiceOrder order, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);

    // ── Events ────────────────────────────────────────────────────────────────

    event EventHandler<IServiceOrder>? OrderAdded;
    event EventHandler<IServiceOrder>? OrderUpdated;
    event EventHandler<(IServiceOrder Order, ServiceOrderStatus Previous)>? OrderStatusChanged;
    event EventHandler<string>?        OrderDeleted;
}
