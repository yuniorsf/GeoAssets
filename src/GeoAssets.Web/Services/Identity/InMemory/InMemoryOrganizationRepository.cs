using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Web.Services.Identity.InMemory;

public sealed class InMemoryOrganizationRepository(WasmIdentityStore store) : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(store.Organizations.FirstOrDefault(o => o.Id == id));

    public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => Task.FromResult(store.Organizations.FirstOrDefault(o => o.Slug == slug));

    public Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Organization>>(store.Organizations.ToList());

    public Task<IReadOnlyList<AppUser>> GetUsersAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppUser>>(
            store.Users.Where(u => u.OrganizationId == organizationId).ToList());

    public Task AddAsync(Organization organization, CancellationToken ct = default)
    {
        store.Organizations.Add(organization);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Organization organization, CancellationToken ct = default)
    {
        var idx = store.Organizations.FindIndex(o => o.Id == organization.Id);
        if (idx >= 0) store.Organizations[idx] = organization;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
