using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFPolicyRepository(GeoIdentityDbContext db) : IPolicyRepository
{
    public Task<AppPolicy?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Policies.Include(p => p.Requirements)
                      .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<AppPolicy?> GetByNameAsync(string name, CancellationToken ct = default)
        => db.Policies.Include(p => p.Requirements)
                      .FirstOrDefaultAsync(p => p.Name == name, ct);

    public async Task<IReadOnlyList<AppPolicy>> GetAllAsync(CancellationToken ct = default)
        => await db.Policies.Include(p => p.Requirements).ToListAsync(ct);

    public async Task AddAsync(AppPolicy policy, CancellationToken ct = default)
        => await db.Policies.AddAsync(policy, ct);

    public Task UpdateAsync(AppPolicy policy, CancellationToken ct = default)
    {
        db.Policies.Update(policy);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Policies.FindAsync([id], ct);
        if (p is not null) db.Policies.Remove(p);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
