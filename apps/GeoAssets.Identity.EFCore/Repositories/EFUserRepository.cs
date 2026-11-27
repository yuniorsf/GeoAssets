using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFUserRepository(GeoIdentityDbContext db, TimeProvider timeProvider) : IUserRepository
{
    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                   .Include(u => u.UserClaims)
                   .FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<AppUser?> GetByExternalObjectIdAsync(string oid, CancellationToken ct = default)
        => db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                   .Include(u => u.UserClaims)
                   .FirstOrDefaultAsync(u => u.ExternalObjectId == oid, ct);

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default)
        => await db.Users.ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default)
        => await db.Users
            .Where(u => u.UserRoles.Any(ur => ur.OrganizationId == null && ur.Role.Name == roleName))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await db.Users.Where(u => u.OrganizationId == organizationId).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default)
        => await db.UserRoles
            .Where(ur => ur.UserId == userId && ur.OrganizationId == null)
            .Select(ur => ur.Role)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(
        Guid userId, CancellationToken ct = default)
        => await db.UserRoles
            .Where(ur => ur.UserId == userId && ur.OrganizationId == null)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task AddAsync(AppUser user, CancellationToken ct = default)
        => await db.Users.AddAsync(user, ct).ConfigureAwait(false);

    /// <summary>
    /// Adds and saves the just-provisioned user. Concurrent first requests of one user all find no row and all insert;
    /// <c>IX_Users_ExternalObjectId</c> lets one through (XD01-475). A losing insert is discarded and the stored user read
    /// back. A failure that left no such user is rethrown.
    /// </summary>
    public async Task<AppUser> ProvisionAsync(AppUser user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var entry = await db.Users.AddAsync(user, ct).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return user;
        }
        catch (DbUpdateException)
        {
            entry.State = EntityState.Detached;
            var stored = await GetByExternalObjectIdAsync(user.ExternalObjectId, ct).ConfigureAwait(false);
            if (stored is null)
                throw;

            return stored;
        }
    }

    public Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        db.Users.Update(user);
        return Task.CompletedTask;
    }

    public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default)
        => AssignAsync(userId, roleId, organizationId: null, assignedBy, ct);

    public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
        => RemoveAsync(userId, roleId, organizationId: null, ct);

    public Task AssignRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, string? assignedBy = null, CancellationToken ct = default)
        => AssignAsync(userId, roleId, organizationId, assignedBy, ct);

    public Task RemoveRoleOnOrganizationAsync(Guid userId, Guid roleId, Guid organizationId, CancellationToken ct = default)
        => RemoveAsync(userId, roleId, organizationId, ct);

    public async Task<IReadOnlyList<UserRole>> GetScopedRoleAssignmentsAsync(Guid userId, CancellationToken ct = default)
        => await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.OrganizationId != null)
            .Include(ur => ur.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<UserRole>> GetRoleAssignmentsOnOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.OrganizationId == organizationId)
            .Include(ur => ur.User)
            .Include(ur => ur.Role)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task AssignAsync(Guid userId, Guid roleId, Guid? organizationId, string? assignedBy, CancellationToken ct)
    {
        var exists = await db.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId && ur.OrganizationId == organizationId, ct)
            .ConfigureAwait(false);
        if (!exists)
            await db.UserRoles.AddAsync(new UserRole { UserId = userId, RoleId = roleId, OrganizationId = organizationId, AssignedBy = assignedBy, AssignedAt = timeProvider.GetUtcNow().UtcDateTime }, ct).ConfigureAwait(false);
    }

    private async Task RemoveAsync(Guid userId, Guid roleId, Guid? organizationId, CancellationToken ct)
    {
        var ur = await db.UserRoles
            .FirstOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId && x.OrganizationId == organizationId, ct)
            .ConfigureAwait(false);
        if (ur is not null) db.UserRoles.Remove(ur);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
