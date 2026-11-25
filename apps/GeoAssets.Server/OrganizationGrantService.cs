using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// The kernel's generic organization-grant writes (XD01-385) behind <see cref="OrganizationGrantsRestApiExtensions"/>: an
/// idempotent upsert and a revoke, both keyed by <see cref="OrganizationGrantKey"/>, and the paged list of one organization
/// pair's active grants — over <see cref="IOrganizationGrantRepository"/>, so every write also lands on the grant-sync feed
/// (XD01-290). It knows no vertical: what a grant means is the caller's business. The store keeps one active grant per key
/// (<c>UX_OrganizationGrants_ActiveKey</c>, XD01-394). Every grant it writes is service-owned
/// (<see cref="OrganizationGrantSources.Service"/>, XD01-411): an upsert of a key an administrator issued takes the grant
/// over, so a vertical's sync never fails on it and the administrator can no longer edit it.
/// </summary>
public sealed class OrganizationGrantService(IOrganizationGrantRepository grants, TimeProvider timeProvider)
{
    /// <summary>
    /// Creates the grant with <c>request.Key</c>, or brings the active one to the request — safe under concurrent upserts of
    /// one key (XD01-394): the store keeps one active grant per key, so when another upsert stores its grant between this
    /// one's read and its save (<see cref="OrganizationGrantKeyConflictException"/>, the failed write already discarded by
    /// the repository), the upsert re-reads the key once and answers <see cref="OrganizationGrantUpsertOutcome.Unchanged"/>
    /// or applies the update (<see cref="OrganizationGrantUpsertOutcome.Updated"/>) — exactly one of the racing callers gets
    /// <see cref="OrganizationGrantUpsertOutcome.Created"/>. A second conflict propagates (the route answers 409).
    /// </summary>
    public async Task<OrganizationGrantUpsertResult> UpsertAsync(OrganizationGrantUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await UpsertOnceAsync(request, ct).ConfigureAwait(false);
        }
        catch (OrganizationGrantKeyConflictException)
        {
            return await UpsertOnceAsync(request, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Deactivates every active grant with <paramref name="key"/> (this repository never hard-deletes).</summary>
    public async Task<OrganizationGrantRevokeResult> RevokeAsync(OrganizationGrantKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        var active = await FindActiveAsync(key, ct).ConfigureAwait(false);
        if (active.Count == 0) return new OrganizationGrantRevokeResult(0);

        foreach (var grant in active)
        {
            grant.IsActive = false;
            await grants.UpdateAsync(grant, ct).ConfigureAwait(false);
        }

        await grants.SaveChangesAsync(ct).ConfigureAwait(false);
        return new OrganizationGrantRevokeResult(active.Count);
    }

    public async Task<OrganizationGrantPage> ListActiveAsync(
        Guid granteeOrganizationId, Guid resourceOrganizationId, int page, int pageSize, CancellationToken ct)
    {
        var active = await grants.GetActiveGrantsAsync(granteeOrganizationId, resourceOrganizationId, ct).ConfigureAwait(false);
        var items = active
            .OrderBy(g => g.GrantedAt)
            .ThenBy(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(OrganizationGrantDto.FromModel)
            .ToList();
        return new OrganizationGrantPage(items, page, pageSize, active.Count);
    }

    private async Task<OrganizationGrantUpsertResult> UpsertOnceAsync(OrganizationGrantUpsertRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var grant = await grants.FindActiveGrantByKeyAsync(request.Key, ct).ConfigureAwait(false);

        // An expired grant still flagged active holds the key's one active slot while granting nothing: retire it (its
        // deactivation lands on the grant-sync feed like any write) so the key can take a new grant.
        if (grant is { ExpiresAt: { } expiresAt } && expiresAt <= now)
        {
            grant.IsActive = false;
            await grants.UpdateAsync(grant, ct).ConfigureAwait(false);
            await grants.SaveChangesAsync(ct).ConfigureAwait(false);
            grant = null;
        }

        if (grant is null)
        {
            var created = new OrganizationGrant
            {
                GranteeOrganizationId = request.Key.GranteeOrganizationId,
                ResourceOrganizationId = request.Key.ResourceOrganizationId,
                ResourceType = request.Key.ResourceType,
                ScopeId = request.Key.ScopeId,
                AllowedActions = [.. request.AllowedActions],
                RequiredRole = request.RequiredRole,
                RequiresScopedRole = request.RequiresScopedRole,
                OrderTypeIds = [.. request.OrderTypeIds],
                ExpiresAt = request.ExpiresAt,
                GrantedBy = request.GrantedBy,
                GrantedAt = now,
                IsActive = true,
                Source = OrganizationGrantSources.Service,
            };
            await grants.AddAsync(created, ct).ConfigureAwait(false);
            await grants.SaveChangesAsync(ct).ConfigureAwait(false);
            return new OrganizationGrantUpsertResult(OrganizationGrantDto.FromModel(created), OrganizationGrantUpsertOutcome.Created);
        }

        if (Matches(grant, request))
            return new OrganizationGrantUpsertResult(OrganizationGrantDto.FromModel(grant), OrganizationGrantUpsertOutcome.Unchanged);

        grant.AllowedActions.Clear();
        grant.AllowedActions.AddRange(request.AllowedActions);
        grant.RequiredRole = request.RequiredRole;
        grant.RequiresScopedRole = request.RequiresScopedRole;
        grant.OrderTypeIds.Clear();
        grant.OrderTypeIds.AddRange(request.OrderTypeIds);
        grant.ExpiresAt = request.ExpiresAt;
        grant.Source = OrganizationGrantSources.Service;
        await grants.UpdateAsync(grant, ct).ConfigureAwait(false);
        await grants.SaveChangesAsync(ct).ConfigureAwait(false);
        return new OrganizationGrantUpsertResult(OrganizationGrantDto.FromModel(grant), OrganizationGrantUpsertOutcome.Updated);
    }

    private async Task<List<OrganizationGrant>> FindActiveAsync(OrganizationGrantKey key, CancellationToken ct)
    {
        var active = await grants
            .GetActiveGrantsAsync(key.GranteeOrganizationId, key.ResourceOrganizationId, ct)
            .ConfigureAwait(false);

        return [.. active
            .Where(g => string.Equals(g.ResourceType, key.ResourceType, StringComparison.Ordinal)
                && string.Equals(g.ScopeId, key.ScopeId, StringComparison.Ordinal))
            .OrderBy(g => g.GrantedAt)
            .ThenBy(g => g.Id)];
    }

    private static bool Matches(OrganizationGrant grant, OrganizationGrantUpsertRequest request) =>
        SameSet(grant.AllowedActions, request.AllowedActions)
        && string.Equals(grant.RequiredRole, request.RequiredRole, StringComparison.Ordinal)
        && grant.ExpiresAt == request.ExpiresAt
        && grant.RequiresScopedRole == request.RequiresScopedRole
        && SameSet(grant.OrderTypeIds, request.OrderTypeIds)
        && string.Equals(grant.Source, OrganizationGrantSources.Service, StringComparison.Ordinal);

    private static bool SameSet(IEnumerable<string> left, IEnumerable<string> right) =>
        left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal), StringComparer.Ordinal);
}
