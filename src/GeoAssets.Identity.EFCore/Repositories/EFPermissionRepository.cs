using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFPermissionRepository(GeoIdentityDbContext db) : IPermissionRepository
{
    public Task<AppPermission?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Permissions.FindAsync([id], ct).AsTask()!;

    public Task<AppPermission?> GetByCodeAsync(string code, CancellationToken ct = default)
        => db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);

    public async Task<IReadOnlyList<AppPermission>> GetAllAsync(CancellationToken ct = default)
        => await db.Permissions.OrderBy(p => p.Resource).ThenBy(p => p.Action).ToListAsync(ct);

    public async Task<IReadOnlyList<AppPermission>> GetByResourceAsync(string resource, CancellationToken ct = default)
        => await db.Permissions.Where(p => p.Resource == resource).ToListAsync(ct);

    public async Task AddAsync(AppPermission permission, CancellationToken ct = default)
        => await db.Permissions.AddAsync(permission, ct);

    public Task UpdateAsync(AppPermission permission, CancellationToken ct = default)
    {
        db.Permissions.Update(permission);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Permissions.FindAsync([id], ct);
        if (p is not null) db.Permissions.Remove(p);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
