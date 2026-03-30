using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFRoleRepository(GeoIdentityDbContext db) : IRoleRepository
{
    public Task<AppRole?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
                   .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<AppRole?> GetByNameAsync(string name, CancellationToken ct = default)
        => db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
                   .FirstOrDefaultAsync(r => r.Name == name, ct);

    public async Task<IReadOnlyList<AppRole>> GetAllAsync(CancellationToken ct = default)
        => await db.Roles.ToListAsync(ct);

    public async Task AddAsync(AppRole role, CancellationToken ct = default)
        => await db.Roles.AddAsync(role, ct);

    public Task UpdateAsync(AppRole role, CancellationToken ct = default)
    {
        db.Roles.Update(role);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await db.Roles.FindAsync([id], ct);
        if (role is not null) db.Roles.Remove(role);
    }

    public async Task GrantPermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
    {
        var exists = await db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, ct);
        if (!exists)
            await db.RolePermissions.AddAsync(new RolePermission { RoleId = roleId, PermissionId = permissionId }, ct);
    }

    public async Task RevokePermissionAsync(Guid roleId, Guid permissionId, CancellationToken ct = default)
    {
        var rp = await db.RolePermissions.FindAsync([roleId, permissionId], ct);
        if (rp is not null) db.RolePermissions.Remove(rp);
    }

    public async Task<IReadOnlyList<AppPermission>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default)
        => await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
