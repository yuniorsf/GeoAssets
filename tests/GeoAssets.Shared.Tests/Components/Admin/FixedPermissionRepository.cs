using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Shared.Tests.Components.Admin;

/// <summary>The permissions a component lists; every other member is unused here.</summary>
internal sealed class FixedPermissionRepository(params AppPermission[] permissions) : IPermissionRepository
{
    public Task<IReadOnlyList<AppPermission>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AppPermission>>(permissions);
    public Task<AppPermission?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AppPermission?> GetByCodeAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppPermission>> GetByResourceAsync(string resource, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(AppPermission permission, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(AppPermission permission, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
