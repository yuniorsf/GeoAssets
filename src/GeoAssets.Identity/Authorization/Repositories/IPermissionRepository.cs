using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

public interface IPermissionRepository
{
    Task<AppPermission?>               GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AppPermission?>               GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<AppPermission>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppPermission>> GetByResourceAsync(string resource, CancellationToken ct = default);

    Task AddAsync(AppPermission permission, CancellationToken ct = default);
    Task UpdateAsync(AppPermission permission, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
