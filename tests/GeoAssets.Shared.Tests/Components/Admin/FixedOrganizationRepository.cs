using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Shared.Tests.Components.Admin;

/// <summary>The organizations a component names; every other member is unused here.</summary>
internal sealed class FixedOrganizationRepository(params Organization[] organizations) : IOrganizationRepository
{
    public Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Organization>>(organizations);
    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AppUser>> GetUsersAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(Organization organization, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(Organization organization, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
