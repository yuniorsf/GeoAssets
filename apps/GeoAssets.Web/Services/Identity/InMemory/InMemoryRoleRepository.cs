using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Web.Services.Identity.InMemory;

public sealed class InMemoryRoleRepository(WasmIdentityStore store) : IRoleRepository
{
    public Task<AppRole?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var role = store.Roles.FirstOrDefault(r => r.Id == id);
        // RolePermissions is never kept in sync by Grant/RevokePermissionAsync below (they only
        // touch the flat store.RolePermissions list) — populate it here from that list so
        // callers (e.g. the identity admin UI, XD01-58) can read a role's current permissions
        // the same way regardless of which IRoleRepository is active.
        if (role is not null)
            role.RolePermissions = [.. store.RolePermissions
                .Where(rp => rp.RoleId == id)
                .Select(rp => new RolePermission { RoleId = rp.RoleId, PermissionId = rp.PermissionId })];
        return Task.FromResult(role);
    }

    public Task<AppRole?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(store.Roles.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<AppRole>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppRole>>(store.Roles);

    public Task AddAsync(AppRole role, CancellationToken ct = default)
    {
        store.Roles.Add(role);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppRole role, CancellationToken ct = default)
    {
        var idx = store.Roles.FindIndex(r => r.Id == role.Id);
        if (idx >= 0) store.Roles[idx] = role;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        store.Roles.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public Task GrantPermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
    {
        if (!store.RolePermissions.Any(rp => rp.RoleId == roleId && rp.PermissionId == permissionId))
            store.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        return Task.CompletedTask;
    }

    public Task RevokePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
    {
        store.RolePermissions.RemoveAll(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AppPermission>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default)
    {
        var permIds = store.RolePermissions.Where(rp => rp.RoleId == roleId).Select(rp => rp.PermissionId).ToHashSet();
        return Task.FromResult<IReadOnlyList<AppPermission>>(store.Permissions.Where(p => permIds.Contains(p.Id)).ToList());
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
