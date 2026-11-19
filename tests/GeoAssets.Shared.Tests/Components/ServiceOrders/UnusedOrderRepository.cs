using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>An <see cref="IServiceOrderRepository"/> a render never calls; any call is a test failure.</summary>
internal sealed class UnusedOrderRepository : IServiceOrderRepository
{
    private static NotSupportedException Unused() => new("The render must not reach the repository.");

    public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw Unused();
    public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => throw Unused();
    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => throw Unused();
    public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => throw Unused();
    public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) => throw Unused();
    public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw Unused();
    public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => throw Unused();
    public Task DeleteAsync(string id, CancellationToken ct = default) => throw Unused();

    public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
    public event EventHandler<IServiceOrder>? OrderUpdated { add { } remove { } }
    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
    public event EventHandler<string>? OrderDeleted { add { } remove { } }
}
