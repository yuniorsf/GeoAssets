using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="OrganizationGrant"/>.
/// </summary>
public interface IOrganizationGrantRepository
{
    Task<OrganizationGrant?>               GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Active, unexpired grants letting <paramref name="granteeOrganizationId"/> reach
    /// <paramref name="resourceOrganizationId"/>'s resources — the hot-path lookup for
    /// cross-org authorization checks.
    /// </summary>
    Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(
        Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default);

    /// <summary>
    /// All active, unexpired grants letting <paramref name="granteeOrganizationId"/> reach
    /// *any* resource organization — used to pre-resolve a principal's grants once (e.g.
    /// <c>ServerWorkflowPrincipalFactory</c>, XD01-22) rather than per-resource, since the
    /// resource being evaluated against isn't known yet at principal-construction time.
    /// </summary>
    Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(
        Guid granteeOrganizationId, CancellationToken ct = default);

    /// <summary>
    /// The grant flagged active with exactly <paramref name="key"/> (resource type and scope compared ordinally, null
    /// matching only null), <b>expired or not</b> — the one row the store's one-active-grant-per-key rule
    /// (<c>UX_OrganizationGrants_ActiveKey</c>, XD01-394) allows, so the one a new grant with that key would collide with.
    /// The oldest by <see cref="OrganizationGrant.GrantedAt"/> (then id) should a store not enforce the rule; null if none.
    /// </summary>
    Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default);

    Task AddAsync(OrganizationGrant grant, CancellationToken ct = default);
    Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default);

    /// <summary>
    /// Saves the pending grant writes and their grant-sync outbox entries as one unit.
    /// </summary>
    /// <exception cref="OrganizationGrantKeyConflictException">
    /// The store refused the save because another active grant already has the key of a grant being saved (XD01-394);
    /// the failed save's grant writes have been discarded.
    /// </exception>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches up to <paramref name="take"/> grant-change sync entries recorded after
    /// <paramref name="afterSequence"/> (XD01-290) — backs the REST endpoint
    /// <see cref="IOrganizationGrantSyncClient"/> implementations poll to mirror grants into an
    /// isolated vertical microservice's own database.
    /// </summary>
    Task<IReadOnlyList<Models.OrganizationGrantSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default);
}
