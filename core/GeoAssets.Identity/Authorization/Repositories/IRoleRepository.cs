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

    /// <summary>
    /// Resolves all permissions granted across the specified <paramref name="roleNames"/> in a
    /// single batch (eliminating the N+1 <see cref="GetByNameAsync"/> + <see cref="GetPermissionsAsync"/>
    /// loop in <see cref="Services.GeoAuthorizationService.GetAuthorizationContextAsync"/>).
    /// Roles not found locally are ignored.
    /// </summary>
    async Task<IReadOnlyList<AppPermission>> GetPermissionsByRoleNamesAsync(
        IReadOnlyList<string> roleNames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roleNames);
        if (roleNames.Count == 0)
            return [];

        var permissions = new List<AppPermission>();
        foreach (var roleName in roleNames)
        {
            var role = await GetByNameAsync(roleName, ct).ConfigureAwait(false);
            if (role is not null)
                permissions.AddRange(await GetPermissionsAsync(role.Id, ct).ConfigureAwait(false));
        }

        return permissions;
    }

    Task SaveChangesAsync(CancellationToken ct = default);
}
