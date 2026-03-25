using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Web.Services.Identity.InMemory;

public sealed class InMemoryPermissionRepository(WasmIdentityStore store) : IPermissionRepository
{
    public Task<AppPermission?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(store.Permissions.FirstOrDefault(p => p.Id == id));

    public Task<AppPermission?> GetByCodeAsync(string code, CancellationToken ct = default)
        => Task.FromResult(store.Permissions.FirstOrDefault(p => p.Code.Equals(code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<AppPermission>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppPermission>>(store.Permissions.OrderBy(p => p.Resource).ThenBy(p => p.Action).ToList());

    public Task<IReadOnlyList<AppPermission>> GetByResourceAsync(string resource, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppPermission>>(store.Permissions.Where(p => p.Resource == resource).ToList());

    public Task AddAsync(AppPermission permission, CancellationToken ct = default)
    {
        store.Permissions.Add(permission);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppPermission permission, CancellationToken ct = default)
    {
        var idx = store.Permissions.FindIndex(p => p.Id == permission.Id);
        if (idx >= 0) store.Permissions[idx] = permission;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        store.Permissions.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
