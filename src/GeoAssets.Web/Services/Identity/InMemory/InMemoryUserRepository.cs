using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Web.Services.Identity;

namespace GeoAssets.Web.Services.Identity.InMemory;

public sealed class InMemoryUserRepository(WasmIdentityStore store) : IUserRepository
{
    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(store.Users.FirstOrDefault(u => u.Id == id));

    public Task<AppUser?> GetByAzureObjectIdAsync(string oid, CancellationToken ct = default)
        => Task.FromResult(store.Users.FirstOrDefault(u => u.AzureObjectId == oid));

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult(store.Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppUser>>(store.Users);

    public Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default)
    {
        var roleId = store.Roles.FirstOrDefault(r => r.Name == roleName)?.Id;
        if (roleId is null)
            return Task.FromResult<IReadOnlyList<AppUser>>([]);

        var userIds = store.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToHashSet();
        return Task.FromResult<IReadOnlyList<AppUser>>(store.Users.Where(u => userIds.Contains(u.Id)).ToList());
    }

    public Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppUser>>(
            store.Users.Where(u => u.OrganizationId == organizationId).ToList());

    public Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default)
    {
        var roleIds = store.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToHashSet();
        return Task.FromResult<IReadOnlyList<AppRole>>(store.Roles.Where(r => roleIds.Contains(r.Id)).ToList());
    }

    public Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        var roleIds = store.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToHashSet();
        var permIds = store.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToHashSet();
        return Task.FromResult<IReadOnlyList<AppPermission>>(store.Permissions.Where(p => permIds.Contains(p.Id)).ToList());
    }

    public Task AddAsync(AppUser user, CancellationToken ct = default)
    {
        store.Users.Add(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        var idx = store.Users.FindIndex(u => u.Id == user.Id);
        if (idx >= 0) store.Users[idx] = user;
        return Task.CompletedTask;
    }

    public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default)
    {
        if (!store.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == roleId))
            store.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId, AssignedBy = assignedBy });
        return Task.CompletedTask;
    }

    public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        store.UserRoles.RemoveAll(ur => ur.UserId == userId && ur.RoleId == roleId);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
