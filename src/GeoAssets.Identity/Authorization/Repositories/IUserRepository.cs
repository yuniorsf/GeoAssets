using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="AppUser"/>.
/// Swap implementations (EF Core, Dapper, remote API) via DI.
/// </summary>
public interface IUserRepository
{
    Task<AppUser?>                  GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AppUser?>                  GetByAzureObjectIdAsync(string oid, CancellationToken ct = default);
    Task<AppUser?>                  GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>>    GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>>    GetByRoleAsync(string roleName, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>>    GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Returns all roles assigned to the user.</summary>
    Task<IReadOnlyList<AppRole>>      GetRolesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns the union of permissions from all roles assigned to the user.</summary>
    Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(AppUser user, CancellationToken ct = default);
    Task UpdateAsync(AppUser user, CancellationToken ct = default);

    Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default);
    Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
