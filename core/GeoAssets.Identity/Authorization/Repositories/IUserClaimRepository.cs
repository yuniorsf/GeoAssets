using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

public interface IUserClaimRepository
{
    Task<IReadOnlyList<UserClaim>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserClaim>> GetByTypeAsync(string claimType, CancellationToken ct = default);
    Task<UserClaim?>               GetAsync(Guid userId, string claimType, CancellationToken ct = default);

    Task AddAsync(UserClaim claim, CancellationToken ct = default);
    Task UpdateAsync(UserClaim claim, CancellationToken ct = default);
    Task RemoveAsync(Guid claimId, CancellationToken ct = default);
    Task RemoveAllAsync(Guid userId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
