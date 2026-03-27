using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="AppGroup"/> and group membership.
/// </summary>
public interface IGroupRepository
{
    Task<AppGroup?>                  GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AppGroup>>    GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppGroup>>    GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Returns all groups the user is a member of.</summary>
    Task<IReadOnlyList<AppGroup>>    GetGroupsForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns all members of the group.</summary>
    Task<IReadOnlyList<AppUser>>     GetMembersAsync(Guid groupId, CancellationToken ct = default);

    Task AddAsync(AppGroup group, CancellationToken ct = default);
    Task UpdateAsync(AppGroup group, CancellationToken ct = default);

    Task AddMemberAsync(Guid groupId, Guid userId, string? addedBy = null, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
