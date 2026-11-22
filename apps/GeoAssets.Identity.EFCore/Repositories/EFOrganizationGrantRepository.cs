using System.Data.Common;
using GeoAssets.Identity.Authorization.EFCore.Configuration;
using GeoAssets.Identity.Authorization.EFCore.Entities;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
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

    public async Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsIssuedByAsync(
        Guid resourceOrganizationId, DateTime utcNow, CancellationToken ct = default)
        => await db.OrganizationGrants
            .AsNoTracking()
            .Where(g => g.ResourceOrganizationId == resourceOrganizationId
                     && g.IsActive
                     && (g.ExpiresAt == null || g.ExpiresAt > utcNow))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return db.OrganizationGrants
            .Where(g => g.GranteeOrganizationId == key.GranteeOrganizationId
                     && g.ResourceOrganizationId == key.ResourceOrganizationId
                     && g.ResourceType == key.ResourceType
                     && g.ScopeId == key.ScopeId
                     && g.IsActive)
            .OrderBy(g => g.GrantedAt)
            .ThenBy(g => g.Id)
            .FirstOrDefaultAsync(ct);
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
                AllowedActions = [.. grant.AllowedActions],
                RequiredRole = grant.RequiredRole,
                RequiresScopedRole = grant.RequiresScopedRole,
                OrderTypeIds = [.. grant.OrderTypeIds],
                RecordedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            },
            ct).ConfigureAwait(false);

    /// <summary>
    /// Saves the unit of work. A save the store refuses because a grant being saved collides with another active grant's
    /// key (<see cref="OrganizationGrantConfiguration.ActiveKeyIndexName"/>, XD01-394 — a concurrent upsert stored its
    /// grant first) throws <see cref="OrganizationGrantKeyConflictException"/> instead, after detaching every tracked grant
    /// and grant-sync outbox entry: the failed save's writes must not ride along on a later save (no outbox row for a grant
    /// that was never stored), and a re-read must materialize the store's rows rather than the stale tracked ones.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsActiveKeyConflict(ex))
        {
            var key = PendingGrantKey();
            DiscardTrackedGrantWrites();
            throw new OrganizationGrantKeyConflictException(key, ex);
        }
    }

    /// <summary>
    /// The unique violation of <see cref="OrganizationGrantConfiguration.ActiveKeyIndexName"/>, told apart without a
    /// provider dependency: PostgreSQL's SQLSTATE 23505 naming the index, or SQLite's unique-constraint failure on the grant
    /// table (no SQLSTATE; SQLite names the columns, not the index).
    /// </summary>
    private static bool IsActiveKeyConflict(DbUpdateException ex) =>
        ex.InnerException is DbException inner
        && ((string.Equals(inner.SqlState, "23505", StringComparison.Ordinal)
                && inner.Message.Contains(OrganizationGrantConfiguration.ActiveKeyIndexName, StringComparison.Ordinal))
            || (inner.SqlState is null
                && inner.Message.Contains("UNIQUE constraint failed: OrganizationGrants.GranteeOrganizationId, ", StringComparison.Ordinal)));

    private OrganizationGrantKey? PendingGrantKey()
    {
        var pending = db.ChangeTracker.Entries<OrganizationGrant>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .ToList();
        return pending.Count == 1
            ? new OrganizationGrantKey(pending[0].GranteeOrganizationId, pending[0].ResourceOrganizationId, pending[0].ResourceType, pending[0].ScopeId)
            : null;
    }

    private void DiscardTrackedGrantWrites()
    {
        foreach (var entry in db.ChangeTracker.Entries<OrganizationGrant>().ToList()) entry.State = EntityState.Detached;
        foreach (var entry in db.ChangeTracker.Entries<OrganizationGrantSyncOutboxRecord>().ToList()) entry.State = EntityState.Detached;
    }

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
                AllowedActions = r.AllowedActions,
                RequiredRole = r.RequiredRole,
                RequiresScopedRole = r.RequiresScopedRole,
                OrderTypeIds = r.OrderTypeIds,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
