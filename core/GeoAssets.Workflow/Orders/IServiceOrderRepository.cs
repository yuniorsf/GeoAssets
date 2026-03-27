namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Persistence and query abstraction for <see cref="IServiceOrder"/>.
/// Swap implementations (in-memory, EF Core, remote API) via DI.
/// </summary>
public interface IServiceOrderRepository
{
    // ── Read ──────────────────────────────────────────────────────────────────

    IServiceOrder?             GetById(string id);
    IReadOnlyList<IServiceOrder> GetAll();
    IReadOnlyList<IServiceOrder> GetRoots();
    IReadOnlyList<IServiceOrder> GetChildren(string parentId);
    IServiceOrder?             GetParent(string childId);

    // ── Filtered queries ──────────────────────────────────────────────────────

    IReadOnlyList<IServiceOrder> GetByStatus(ServiceOrderStatus status);
    IReadOnlyList<IServiceOrder> GetByAssignee(string userId);
    IReadOnlyList<IServiceOrder> GetByCreator(string userId);
    IReadOnlyList<IServiceOrder> GetByOrderType(string orderTypeId);
    IReadOnlyList<IServiceOrder> GetByDateRange(DateTime from, DateTime to);

    /// <summary>
    /// Returns all orders dispatched to the given target
    /// (user ID, group ID, or organization ID depending on <paramref name="targetType"/>).
    /// </summary>
    IReadOnlyList<IServiceOrder> GetDispatchedTo(string targetId, DispatchTargetType targetType);

    // ── Write ─────────────────────────────────────────────────────────────────

    void Add(IServiceOrder order);
    void Update(IServiceOrder order);
    void Delete(string id);

    // ── Events ────────────────────────────────────────────────────────────────

    event EventHandler<IServiceOrder>? OrderAdded;
    event EventHandler<IServiceOrder>? OrderUpdated;
    event EventHandler<(IServiceOrder Order, ServiceOrderStatus Previous)>? OrderStatusChanged;
    event EventHandler<string>?        OrderDeleted;
}
