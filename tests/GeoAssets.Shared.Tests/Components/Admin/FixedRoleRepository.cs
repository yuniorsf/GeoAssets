using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Shared.Tests.Components.Admin;

/// <summary>The roles a component lists; every other member is unused here.</summary>
internal sealed class FixedRoleRepository(params AppRole[] roles) : IRoleRepository
{
    public Task<IReadOnlyList<AppRole>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AppRole>>(roles);
    public Task<AppRole?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AppRole?> GetByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(AppRole role, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(AppRole role, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task GrantPermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RevokePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppPermission>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
