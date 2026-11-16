using GeoAssets.Identity.Authorization.EFCore.Entities;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Identity.Authorization.EFCore.Repositories;

public sealed class EFOrganizationGrantRepository(GeoIdentityDbContext db, TimeProvider timeProvider) : IOrganizationGrantRepository
{
    public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.OrganizationGrants.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default)
        => await db.OrganizationGrants.ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(
        Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await db.OrganizationGrants
            .Where(g => g.GranteeOrganizationId == granteeOrganizationId
                     && g.ResourceOrganizationId == resourceOrganizationId
                     && g.IsActive
                     && (g.ExpiresAt == null || g.ExpiresAt > now))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(
        Guid granteeOrganizationId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await db.OrganizationGrants
            .Where(g => g.GranteeOrganizationId == granteeOrganizationId
                     && g.IsActive
                     && (g.ExpiresAt == null || g.ExpiresAt > now))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(OrganizationGrant grant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        await db.OrganizationGrants.AddAsync(grant, ct).ConfigureAwait(false);
        await QueueSyncOutboxEntryAsync(grant, ct).ConfigureAwait(false);
    }

    public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        db.OrganizationGrants.Update(grant);
        return QueueSyncOutboxEntryAsync(grant, ct);
    }

    /// <summary>
    /// Records a full snapshot of <paramref name="grant"/>'s post-write state in
    /// <c>OrganizationGrantSyncOutbox</c> (XD01-290), in the same unit of work as the grant
    /// write itself (both flush on the caller's single <see cref="SaveChangesAsync"/>) — the
    /// feed <c>OrganizationGrantSyncWorker</c> polls to mirror grants into the now-isolated
    /// <c>geoassets_fieldservice</c> database for its local RLS policy to check.
    /// </summary>
    private async Task QueueSyncOutboxEntryAsync(OrganizationGrant grant, CancellationToken ct)
        => await db.OrganizationGrantSyncOutbox.AddAsync(
            new OrganizationGrantSyncOutboxRecord
            {
                GrantId = grant.Id,
                GranteeOrganizationId = grant.GranteeOrganizationId,
                ResourceOrganizationId = grant.ResourceOrganizationId,
                ResourceType = grant.ResourceType,
                ScopeId = grant.ScopeId,
                IsActive = grant.IsActive,
                ExpiresAt = grant.ExpiresAt,
                RecordedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            },
            ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default)
        => await db.OrganizationGrantSyncOutbox
            .Where(r => r.SequenceNumber > afterSequence)
            .OrderBy(r => r.SequenceNumber)
            .Take(take)
            .Select(r => new OrganizationGrantSyncEntry
            {
                SequenceNumber = r.SequenceNumber,
                GrantId = r.GrantId,
                GranteeOrganizationId = r.GranteeOrganizationId,
                ResourceOrganizationId = r.ResourceOrganizationId,
                ResourceType = r.ResourceType,
                ScopeId = r.ScopeId,
                IsActive = r.IsActive,
                ExpiresAt = r.ExpiresAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
