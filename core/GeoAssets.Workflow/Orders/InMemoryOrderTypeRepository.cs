namespace GeoAssets.Workflow.Orders;

/// <summary>
/// In-memory implementation of <see cref="IOrderTypeRepository"/>.
/// Used for Blazor WASM hosts and unit tests.
/// </summary>
public sealed class InMemoryOrderTypeRepository : IOrderTypeRepository
{
    private readonly Dictionary<string, OrderType> _store =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<OrderType?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<OrderType>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrderType>>([.. _store.Values]);

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
