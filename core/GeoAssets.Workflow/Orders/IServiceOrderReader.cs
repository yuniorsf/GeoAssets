namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Read-only query surface over <see cref="IServiceOrder"/>. Segregated from
/// <see cref="IServiceOrderWriter"/> so a consumer that only ever reads orders
/// (a reporting view, a read replica, a cache) can depend on this alone, and an
/// implementation that can't support writes isn't forced to stub them out.
/// <see cref="IServiceOrderRepository"/> composes both for the common case of
/// needing full read/write access.
/// </summary>
public interface IServiceOrderReader
{
    // ── Read ──────────────────────────────────────────────────────────────────

    Task<IServiceOrder?>               GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default);
    Task<IServiceOrder?>               GetParentAsync(string childId, CancellationToken ct = default);

    // ── Filtered queries ──────────────────────────────────────────────────────

    Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default);
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
}
