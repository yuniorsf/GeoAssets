using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// The kernel's generic organization-grant writes (XD01-385) behind <see cref="OrganizationGrantsRestApiExtensions"/>: an
/// idempotent upsert and a revoke, both keyed by <see cref="OrganizationGrantKey"/>, and the paged list of one organization
/// pair's active grants — over <see cref="IOrganizationGrantRepository"/>, so every write also lands on the grant-sync feed
/// (XD01-290). It knows no vertical: what a grant means is the caller's business.
/// </summary>
public sealed class OrganizationGrantService(IOrganizationGrantRepository grants, TimeProvider timeProvider)
{
    public async Task<OrganizationGrantUpsertResult> UpsertAsync(OrganizationGrantUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await FindActiveAsync(request.Key, ct).ConfigureAwait(false);
        if (existing.Count == 0)
        {
            var created = new OrganizationGrant
            {
                GranteeOrganizationId = request.Key.GranteeOrganizationId,
                ResourceOrganizationId = request.Key.ResourceOrganizationId,
                ResourceType = request.Key.ResourceType,
                ScopeId = request.Key.ScopeId,
                AllowedActions = [.. request.AllowedActions],
                RequiredRole = request.RequiredRole,
                ExpiresAt = request.ExpiresAt,
                GrantedBy = request.GrantedBy,
                GrantedAt = timeProvider.GetUtcNow().UtcDateTime,
                IsActive = true,
            };
            await grants.AddAsync(created, ct).ConfigureAwait(false);
            await grants.SaveChangesAsync(ct).ConfigureAwait(false);
            return new OrganizationGrantUpsertResult(OrganizationGrantDto.FromModel(created), OrganizationGrantUpsertOutcome.Created);
        }

        var grant = existing[0];
        if (Matches(grant, request))
            return new OrganizationGrantUpsertResult(OrganizationGrantDto.FromModel(grant), OrganizationGrantUpsertOutcome.Unchanged);

        grant.AllowedActions.Clear();
        grant.AllowedActions.AddRange(request.AllowedActions);
        grant.RequiredRole = request.RequiredRole;
        grant.ExpiresAt = request.ExpiresAt;
        await grants.UpdateAsync(grant, ct).ConfigureAwait(false);
        await grants.SaveChangesAsync(ct).ConfigureAwait(false);
        return new OrganizationGrantUpsertResult(OrganizationGrantDto.FromModel(grant), OrganizationGrantUpsertOutcome.Updated);
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
        grant.AllowedActions.Order(StringComparer.Ordinal).SequenceEqual(request.AllowedActions.Order(StringComparer.Ordinal), StringComparer.Ordinal)
        && string.Equals(grant.RequiredRole, request.RequiredRole, StringComparison.Ordinal)
        && grant.ExpiresAt == request.ExpiresAt;
}
