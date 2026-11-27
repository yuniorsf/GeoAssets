using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The <see cref="TileCacheScope"/> of a tile request (XD01-434): the caller's organization (its id, <c>N</c> form — all
/// zeros for an organization-less caller, who sees the shared features only) and the hash of the active, unexpired
/// cross-organization grants its organization holds (XD01-408/409/411; <see cref="TileCacheScope.HashGrants"/> of each
/// grant's resource organization, resource type, scope and permissions). The tile sources read the caller's organization's
/// and the shared rows today; the grants are in the scope anyway, so a source that someday reads what a grant reaches can
/// never serve one caller's tile to another whose grants differ.
/// </summary>
public static class TileScopeResolver
{
    /// <summary>The scope of the request whose tenant is <paramref name="tenant"/>, its grants read from <paramref name="grants"/> (<c>null</c> = none).</summary>
    public static async Task<TileCacheScope> ResolveAsync(ITenantContext? tenant, IOrganizationGrantRepository? grants, CancellationToken ct)
    {
        var organizationId = tenant?.CurrentOrganizationId ?? Guid.Empty;
        var organization = organizationId.ToString("N", CultureInfo.InvariantCulture);
        if (organizationId == Guid.Empty || grants is null)
            return TileCacheScope.Create(organization, []);

        var active = await grants.GetActiveGrantsForGranteeAsync(organizationId, ct).ConfigureAwait(false);
        return TileCacheScope.Create(organization, active.Select(g => string.Join(
            '|',
            g.ResourceOrganizationId.ToString("N", CultureInfo.InvariantCulture),
            g.ResourceType ?? string.Empty,
            g.ScopeId ?? string.Empty,
            string.Join(',', g.AllowedActions.Order(StringComparer.Ordinal)))));
    }

    /// <summary>
    /// The organizations other than <paramref name="organizationId"/> whose scope may show its features (XD01-458): the
    /// grantees of its active grants unexpired at <paramref name="utcNow"/>, read from <paramref name="grants"/>
    /// (<c>null</c> = none) — the scopes a write of its features dirties besides its own.
    /// </summary>
    public static async Task<IReadOnlyCollection<Guid>> GranteesAsync(
        Guid organizationId, IOrganizationGrantRepository? grants, DateTime utcNow, CancellationToken ct)
    {
        if (organizationId == Guid.Empty || grants is null) return [];

        var issued = await grants.GetActiveGrantsIssuedByAsync(organizationId, utcNow, ct).ConfigureAwait(false);
        return [.. issued
            .Select(g => g.GranteeOrganizationId)
            .Where(id => id != organizationId && id != Guid.Empty)
            .Distinct()];
    }
}
