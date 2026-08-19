using GeoAssets.Identity.Authorization.Models;

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

    Task AddAsync(OrganizationGrant grant, CancellationToken ct = default);
    Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
