using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

public interface IRoleRepository
{
    Task<AppRole?>               GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AppRole?>               GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<AppRole>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(AppRole role, CancellationToken ct = default);
    Task UpdateAsync(AppRole role, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task GrantPermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default);
    Task RevokePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default);

    Task<IReadOnlyList<AppPermission>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
