using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="PendingInvitation"/>.
/// </summary>
public interface IPendingInvitationRepository
{
    Task<PendingInvitation?>               GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Used by the redirect gate in XD01-71 to match a freshly-authenticated user back to their invitation.</summary>
    Task<PendingInvitation?>               GetByExternalObjectIdAsync(string externalObjectId, CancellationToken ct = default);

    Task<IReadOnlyList<PendingInvitation>> GetAllPendingAsync(CancellationToken ct = default);

    Task AddAsync(PendingInvitation invitation, CancellationToken ct = default);
    Task UpdateAsync(PendingInvitation invitation, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
