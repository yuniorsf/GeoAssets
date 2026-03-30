using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFUserRepository(GeoIdentityDbContext db) : IUserRepository
{
    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                   .Include(u => u.UserClaims)
                   .FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<AppUser?> GetByAzureObjectIdAsync(string oid, CancellationToken ct = default)
        => db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                   .Include(u => u.UserClaims)
                   .FirstOrDefaultAsync(u => u.AzureObjectId == oid, ct);

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default)
        => await db.Users.ToListAsync(ct);

    public async Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default)
        => await db.Users
            .Where(u => u.UserRoles.Any(ur => ur.Role.Name == roleName))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await db.Users.Where(u => u.OrganizationId == organizationId).ToListAsync(ct);

    public async Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default)
        => await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(
        Guid userId, CancellationToken ct = default)
        => await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync(ct);

    public async Task AddAsync(AppUser user, CancellationToken ct = default)
        => await db.Users.AddAsync(user, ct);

    public Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        db.Users.Update(user);
        return Task.CompletedTask;
    }

    public async Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default)
    {
        var exists = await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);
        if (!exists)
            await db.UserRoles.AddAsync(new UserRole { UserId = userId, RoleId = roleId, AssignedBy = assignedBy }, ct);
    }

    public async Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        var ur = await db.UserRoles.FindAsync([userId, roleId], ct);
        if (ur is not null) db.UserRoles.Remove(ur);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
