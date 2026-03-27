using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Web.Services.Identity.InMemory;

public sealed class InMemoryUserClaimRepository(WasmIdentityStore store) : IUserClaimRepository
{
    public Task<IReadOnlyList<UserClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UserClaim>>(store.UserClaims.Where(c => c.UserId == userId).ToList());

    public Task<IReadOnlyList<UserClaim>> GetByTypeAsync(string claimType, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UserClaim>>(store.UserClaims.Where(c => c.Type == claimType).ToList());

    public Task<UserClaim?> GetAsync(Guid userId, string claimType, CancellationToken ct = default)
        => Task.FromResult(store.UserClaims.FirstOrDefault(c => c.UserId == userId && c.Type == claimType));

    public Task AddAsync(UserClaim claim, CancellationToken ct = default)
    {
        store.UserClaims.Add(claim);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserClaim claim, CancellationToken ct = default)
    {
        var idx = store.UserClaims.FindIndex(c => c.Id == claim.Id);
        if (idx >= 0) store.UserClaims[idx] = claim;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid claimId, CancellationToken ct = default)
    {
        store.UserClaims.RemoveAll(c => c.Id == claimId);
        return Task.CompletedTask;
    }

    public Task RemoveAllAsync(Guid userId, CancellationToken ct = default)
    {
        store.UserClaims.RemoveAll(c => c.UserId == userId);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
