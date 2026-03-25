using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

public interface IPolicyRepository
{
    Task<AppPolicy?>               GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AppPolicy?>               GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<AppPolicy>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(AppPolicy policy, CancellationToken ct = default);
    Task UpdateAsync(AppPolicy policy, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
