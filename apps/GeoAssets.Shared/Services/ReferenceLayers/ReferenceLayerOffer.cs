using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>
/// Which organizations the map offers as a reference layer (XD01-331): exactly those for which the kernel's
/// <c>OrganizationResourceAccess</c> would open <c>GET …/organizations/{id}/reference-features</c> (XD01-330), decided from
/// the same inputs — the caller's home context (<c>/api/identity/me</c>) and cross-organization entitlements
/// (<c>/api/identity/me/cross-org-entitlements</c>, XD01-329). The server stays the authority; this only keeps the client
/// from offering what the server would refuse.
///
/// <para>The rule, as the server applies it for <see cref="Permission"/> on the organization's <see cref="GeoFeature"/>s:
/// another organization (never the caller's own, nor <see cref="Guid.Empty"/>; a caller with no organization gets none) is
/// offered when one of its active, unexpired grants covers the resource type (<see cref="GeoFeature"/>, or no type at
/// all — a grant for "Feature" or any other type opens nothing), lists no order type, lists the permission among its
/// actions (both compared ignoring case, <see cref="OrganizationGrantMatching"/>), and the caller qualifies: for an
/// organization-wide grant through the permission held at home and, when the grant names a role, that role at home; for a
/// <see cref="CrossOrgGrantEntitlementDto.RequiresScopedRole"/> grant through the permission and the named role held on
/// that organization. A grant's <c>ScopeId</c> narrows what the server returns, not whether it opens the layer.</para>
/// </summary>
public static class ReferenceLayerOffer
{
    /// <summary>The permission a grant must list (<c>ReferenceFeaturesRestApiExtensions.Permission</c> on the server).</summary>
    public const string Permission = "features:reference";

    /// <summary>The resource type the server resolves the route for.</summary>
    public const string ResourceType = nameof(GeoFeature);

    /// <summary>
    /// Whether any grant in <paramref name="entitlements"/> could open a reference layer whoever holds it — the cheap
    /// check that spares a caller with no such grant the home-context lookup.
    /// </summary>
    public static bool AnyCandidate(CrossOrgEntitlementsDto entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        return (entitlements.Grants ?? []).Any(IsCandidate);
    }

    /// <summary>
    /// The organizations to offer, by name: those for which a grant of <paramref name="entitlements"/> qualifies the
    /// caller described by <paramref name="home"/> (its own organization, home roles and permissions) at
    /// <paramref name="utcNow"/>. An organization the entitlements do not name is shown by its id.
    /// </summary>
    public static IReadOnlyList<ReferenceOrganization> Organizations(
        AuthorizationContext home, CrossOrgEntitlementsDto entitlements, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(entitlements);

        if (home.User.OrganizationId is not { } ownOrganizationId) return [];

        // The context the server evaluates: home roles and permissions, plus those held on other organizations.
        var context = new AuthorizationContext
        {
            User = home.User,
            Roles = home.Roles,
            Claims = home.Claims,
            Permissions = home.Permissions,
            ScopedRoles = entitlements.ToScopedRoles(),
            ScopedPermissions = entitlements.ToScopedPermissions(),
        };
        var hasHomePermission = context.HasPermission(Permission);

        return [.. (entitlements.Grants ?? [])
            .Where(g => g.ResourceOrganizationId != Guid.Empty && g.ResourceOrganizationId != ownOrganizationId)
            .Where(g => g.ExpiresAt is not { } expiresAt || expiresAt > utcNow)
            .Where(IsCandidate)
            .Where(g => g.RequiresScopedRole
                ? context.HasPermissionOn(g.ResourceOrganizationId, Permission) &&
                  (g.RequiredRole is null || context.HasRoleOn(g.ResourceOrganizationId, g.RequiredRole))
                : hasHomePermission && (g.RequiredRole is null || context.HasRole(g.RequiredRole)))
            .Select(g => g.ResourceOrganizationId)
            .Distinct()
            .Select(id => new ReferenceOrganization(id, entitlements.OrganizationName(id) ?? id.ToString()))
            .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(o => o.Id)];
    }

    private static bool IsCandidate(CrossOrgGrantEntitlementDto grant) =>
        OrganizationGrantMatching.CoversResourceType(grant.ResourceType, ResourceType) &&
        (grant.OrderTypeIds ?? []).Count == 0 &&
        OrganizationGrantMatching.AllowsAction(grant.AllowedActions ?? [], Permission);
}
