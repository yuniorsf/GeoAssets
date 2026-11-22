using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Shared.Tests.Components.Admin;

/// <summary>Never called by <c>UserDetail</c>'s render; only injected.</summary>
internal sealed class UnusedUserRepository : IUserRepository
{
    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AppUser?> GetByExternalObjectIdAsync(string oid, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(AppUser user, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(AppUser user, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AssignRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, string? assignedBy = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<UserRole>> GetScopedRoleAssignmentsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<UserRole>> GetRoleAssignmentsOnOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
