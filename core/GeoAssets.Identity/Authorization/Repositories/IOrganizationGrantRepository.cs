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

    Task AddAsync(OrganizationGrant grant, CancellationToken ct = default);
    Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
