using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFOrganizationRepository(GeoIdentityDbContext db) : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Organizations.Include(o => o.Users)
                           .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => db.Organizations.FirstOrDefaultAsync(o => o.Slug == slug, ct);

    public async Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default)
        => await db.Organizations.ToListAsync(ct);

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync(Guid organizationId, CancellationToken ct = default)
        => await db.Users.Where(u => u.OrganizationId == organizationId).ToListAsync(ct);

    public async Task AddAsync(Organization organization, CancellationToken ct = default)
        => await db.Organizations.AddAsync(organization, ct);

    public Task UpdateAsync(Organization organization, CancellationToken ct = default)
    {
        db.Organizations.Update(organization);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
