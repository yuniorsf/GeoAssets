using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFOrganizationGrantRepository(GeoIdentityDbContext db) : IOrganizationGrantRepository
{
    public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.OrganizationGrants.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default)
        => await db.OrganizationGrants.ToListAsync(ct);

    public async Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(
        Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db.OrganizationGrants
            .Where(g => g.GranteeOrganizationId == granteeOrganizationId
                     && g.ResourceOrganizationId == resourceOrganizationId
                     && g.IsActive
                     && (g.ExpiresAt == null || g.ExpiresAt > now))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(
        Guid granteeOrganizationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db.OrganizationGrants
            .Where(g => g.GranteeOrganizationId == granteeOrganizationId
                     && g.IsActive
                     && (g.ExpiresAt == null || g.ExpiresAt > now))
            .ToListAsync(ct);
    }

    public async Task AddAsync(OrganizationGrant grant, CancellationToken ct = default)
        => await db.OrganizationGrants.AddAsync(grant, ct);

    public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default)
    {
        db.OrganizationGrants.Update(grant);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
