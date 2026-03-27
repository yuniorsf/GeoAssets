using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="Organization"/>.
/// </summary>
public interface IOrganizationRepository
{
    Task<Organization?>                  GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Organization?>                  GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Organization>>    GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns all users that belong to the given organization.</summary>
    Task<IReadOnlyList<AppUser>>         GetUsersAsync(Guid organizationId, CancellationToken ct = default);

    Task AddAsync(Organization organization, CancellationToken ct = default);
    Task UpdateAsync(Organization organization, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
