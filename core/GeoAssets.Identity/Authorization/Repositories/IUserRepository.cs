using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="AppUser"/>.
/// Swap implementations (EF Core, Dapper, remote API) via DI.
/// </summary>
public interface IUserRepository
{
    Task<AppUser?>                  GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AppUser?>                  GetByExternalObjectIdAsync(string oid, CancellationToken ct = default);
    Task<AppUser?>                  GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>>    GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>>    GetByRoleAsync(string roleName, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>>    GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Returns the roles assigned to the user in their home organization.</summary>
    Task<IReadOnlyList<AppRole>>      GetRolesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns the union of permissions from the roles assigned to the user in their home organization.</summary>
    Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(AppUser user, CancellationToken ct = default);
    Task UpdateAsync(AppUser user, CancellationToken ct = default);

    Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default);
    Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);

    // ── Organization-scoped assignments (XD01-325) ────────────────────────────

    /// <summary>
    /// Gives the user the role on <paramref name="organizationId"/>, independently of any
    /// home-scoped assignment of the same role. Assigning the same (user, role, organization)
    /// again is a no-op.
    /// </summary>
    Task AssignRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, string? assignedBy = null, CancellationToken ct = default);

    /// <summary>Revokes the role the user holds on <paramref name="organizationId"/>, leaving every other scope untouched.</summary>
    Task RemoveRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Returns the user's organization-scoped assignments (never the home-scoped ones), each with
    /// its <see cref="UserRole.Role"/> and that role's permissions loaded.
    /// </summary>
    Task<IReadOnlyList<UserRole>> GetScopedRoleAssignmentsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the assignments scoped to <paramref name="organizationId"/>, each with its
    /// <see cref="UserRole.User"/> and <see cref="UserRole.Role"/> loaded.
    /// </summary>
    Task<IReadOnlyList<UserRole>> GetRoleAssignmentsOnOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
