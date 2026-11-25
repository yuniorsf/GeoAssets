using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// The writes an administrator of a resource organization makes to the grants it issues (XD01-411), behind
/// <see cref="OrganizationIssuedGrantsRestApiExtensions"/>: issue, edit the rules of, and revoke. Every grant they create is
/// administrator-owned (<see cref="OrganizationGrantSources.Administrator"/>). A service-owned grant, which a vertical host
/// provisioned through <see cref="OrganizationGrantService"/>, is never changed here: it answers
/// <see cref="IssuedGrantOutcome.ServiceOwned"/>, whether it is the grant named or the active grant with the key to issue.
/// Writes go through <see cref="IOrganizationGrantRepository"/>, so they land on the grant-sync feed (XD01-290) like any
/// other; a create that loses a race for its key (XD01-394) propagates <see cref="OrganizationGrantKeyConflictException"/>.
/// </summary>
public sealed class IssuedOrganizationGrantService(IOrganizationGrantRepository grants, TimeProvider timeProvider)
{
    public async Task<IssuedGrantResult> IssueAsync(
        Guid resourceOrganizationId, IssuedGrantCreateRequest request, string grantedBy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantedBy);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var key = request.Key(resourceOrganizationId);
        var existing = await grants.FindActiveGrantByKeyAsync(key, ct).ConfigureAwait(false);

        // An expired grant still flagged active holds the key's one active slot while granting nothing: retire it, as the
        // service upsert does, so the key can take the new grant.
        if (existing is { ExpiresAt: { } expiresAt } && expiresAt <= now)
        {
            existing.IsActive = false;
            await grants.UpdateAsync(existing, ct).ConfigureAwait(false);
            await grants.SaveChangesAsync(ct).ConfigureAwait(false);
            existing = null;
        }

        if (existing is not null)
            return new IssuedGrantResult(IsServiceOwned(existing) ? IssuedGrantOutcome.ServiceOwned : IssuedGrantOutcome.Exists, existing);

        var rules = request.Rules;
        var created = new OrganizationGrant
        {
            GranteeOrganizationId = key.GranteeOrganizationId,
            ResourceOrganizationId = key.ResourceOrganizationId,
            ResourceType = key.ResourceType,
            ScopeId = key.ScopeId,
            AllowedActions = [.. rules.AllowedActions],
            RequiredRole = rules.RequiredRole,
            RequiresScopedRole = rules.RequiresScopedRole,
            OrderTypeIds = [.. rules.OrderTypeIds],
            ExpiresAt = rules.ExpiresAt,
            GrantedBy = grantedBy,
            GrantedAt = now,
            IsActive = true,
            Source = OrganizationGrantSources.Administrator,
        };
        await grants.AddAsync(created, ct).ConfigureAwait(false);
        await grants.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IssuedGrantResult(IssuedGrantOutcome.Created, created);
    }

    public async Task<IssuedGrantResult> UpdateAsync(
        Guid resourceOrganizationId, Guid grantId, IssuedGrantRules rules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var grant = await FindIssuedAsync(resourceOrganizationId, grantId, ct).ConfigureAwait(false);
        if (grant is null) return new IssuedGrantResult(IssuedGrantOutcome.NotFound, null);
        if (IsServiceOwned(grant)) return new IssuedGrantResult(IssuedGrantOutcome.ServiceOwned, grant);
        if (Matches(grant, rules)) return new IssuedGrantResult(IssuedGrantOutcome.Unchanged, grant);

        grant.AllowedActions.Clear();
        grant.AllowedActions.AddRange(rules.AllowedActions);
        grant.RequiredRole = rules.RequiredRole;
        grant.RequiresScopedRole = rules.RequiresScopedRole;
        grant.OrderTypeIds.Clear();
        grant.OrderTypeIds.AddRange(rules.OrderTypeIds);
        grant.ExpiresAt = rules.ExpiresAt;
        await grants.UpdateAsync(grant, ct).ConfigureAwait(false);
        await grants.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IssuedGrantResult(IssuedGrantOutcome.Updated, grant);
    }

    /// <summary>Deactivates the grant (this repository never hard-deletes).</summary>
    public async Task<IssuedGrantResult> RevokeAsync(Guid resourceOrganizationId, Guid grantId, CancellationToken ct)
    {
        var grant = await FindIssuedAsync(resourceOrganizationId, grantId, ct).ConfigureAwait(false);
        if (grant is null) return new IssuedGrantResult(IssuedGrantOutcome.NotFound, null);
        if (IsServiceOwned(grant)) return new IssuedGrantResult(IssuedGrantOutcome.ServiceOwned, grant);

        grant.IsActive = false;
        await grants.UpdateAsync(grant, ct).ConfigureAwait(false);
        await grants.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IssuedGrantResult(IssuedGrantOutcome.Revoked, grant);
    }

    /// <summary>The active, unexpired grant <paramref name="grantId"/> issued by <paramref name="resourceOrganizationId"/>, or null.</summary>
    private async Task<OrganizationGrant?> FindIssuedAsync(Guid resourceOrganizationId, Guid grantId, CancellationToken ct)
    {
        var grant = await grants.GetByIdAsync(grantId, ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return grant is { IsActive: true }
               && grant.ResourceOrganizationId == resourceOrganizationId
               && (grant.ExpiresAt is null || grant.ExpiresAt > now)
            ? grant
            : null;
    }

    private static bool IsServiceOwned(OrganizationGrant grant) =>
        !string.Equals(grant.Source, OrganizationGrantSources.Administrator, StringComparison.Ordinal);

    private static bool Matches(OrganizationGrant grant, IssuedGrantRules rules) =>
        SameSet(grant.AllowedActions, rules.AllowedActions)
        && string.Equals(grant.RequiredRole, rules.RequiredRole, StringComparison.Ordinal)
        && grant.ExpiresAt == rules.ExpiresAt
        && grant.RequiresScopedRole == rules.RequiresScopedRole
        && SameSet(grant.OrderTypeIds, rules.OrderTypeIds);

    private static bool SameSet(IEnumerable<string> left, IEnumerable<string> right) =>
        left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal), StringComparer.Ordinal);
}
