using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFUserClaimRepository(GeoIdentityDbContext db) : IUserClaimRepository
{
    public async Task<IReadOnlyList<UserClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await db.UserClaims.Where(c => c.UserId == userId).ToListAsync(ct);

    public async Task<IReadOnlyList<UserClaim>> GetByTypeAsync(string claimType, CancellationToken ct = default)
        => await db.UserClaims.Where(c => c.Type == claimType).ToListAsync(ct);

    public Task<UserClaim?> GetAsync(Guid userId, string claimType, CancellationToken ct = default)
        => db.UserClaims.FirstOrDefaultAsync(c => c.UserId == userId && c.Type == claimType, ct);

    public async Task AddAsync(UserClaim claim, CancellationToken ct = default)
        => await db.UserClaims.AddAsync(claim, ct);

    public Task UpdateAsync(UserClaim claim, CancellationToken ct = default)
    {
        db.UserClaims.Update(claim);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(Guid claimId, CancellationToken ct = default)
    {
        var c = await db.UserClaims.FindAsync([claimId], ct);
        if (c is not null) db.UserClaims.Remove(c);
    }

    public async Task RemoveAllAsync(Guid userId, CancellationToken ct = default)
    {
        var claims = await db.UserClaims.Where(c => c.UserId == userId).ToListAsync(ct);
        db.UserClaims.RemoveRange(claims);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
