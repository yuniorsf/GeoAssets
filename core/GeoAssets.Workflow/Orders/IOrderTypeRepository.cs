namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Persistence abstraction for <see cref="OrderType"/> definitions.
///
/// Implementations:
///   • <c>EFOrderTypeRepository</c> — EF Core (SQL Server, SQLite, etc.)
///   • <c>InMemoryOrderTypeRepository</c> — in-memory for tests and WASM
/// </summary>
public interface IOrderTypeRepository
{
    Task<OrderType?>                 GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<OrderType>>   GetAllAsync(CancellationToken ct = default);

    Task AddAsync(OrderType orderType, CancellationToken ct = default);
    Task UpdateAsync(OrderType orderType, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
