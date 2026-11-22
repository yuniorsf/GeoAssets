using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// The one decision of what the caller may do on an organization's resources (XD01-21, XD01-174, XD01-326), taken from
/// the organization alone — before any resource is loaded (XD01-330). <see cref="OrgResourceAuthorizationHandler"/>
/// applies it to a resource it was handed; an endpoint that reads an organization's resources through a query decides
/// with it first and reads only what the answer covers.
///
/// <para>The rule: on the caller's own organization, or on unowned resources (<see cref="Guid.Empty"/>), the caller's home
/// permission decides for all of them. On another organization, the caller's active grants from it whose resource type
/// and listed actions match, that list no order type, and that the caller qualifies for: an organization-wide grant
/// (<see cref="OrganizationGrant.RequiresScopedRole"/> false) through the home permission and home roles, a scoped-role
/// grant through the permission and roles held on that organization only. A matching grant without a
/// <see cref="OrganizationGrant.ScopeId"/> covers all of the organization's resources of that type; otherwise the answer
/// is the set of the matching grants' scopes.</para>
/// </summary>
public sealed class OrganizationResourceAccess(
    IGeoAuthorizationService authorizationService,
    IOrganizationGrantRepository grantRepository)
{
    /// <summary>
    /// What the caller may reach with <paramref name="permissionCode"/> among <paramref name="resourceOrganizationId"/>'s
    /// resources of type <paramref name="resourceType"/>. The caller must be authenticated.
    /// </summary>
    public async Task<OrganizationResourceScope> ResolveAsync(
        Guid resourceOrganizationId, string resourceType, string permissionCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        var hasHomePermission = await authorizationService.HasPermissionAsync(permissionCode, ct).ConfigureAwait(false);

        if (resourceOrganizationId == Guid.Empty)
            return hasHomePermission ? OrganizationResourceScope.All : OrganizationResourceScope.None;

        var authContext = await authorizationService.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
        var userOrganizationId = authContext.User.OrganizationId;

        if (userOrganizationId == resourceOrganizationId)
            return hasHomePermission ? OrganizationResourceScope.All : OrganizationResourceScope.None;

        // Every grant needs the permission somewhere: at home (organization-wide) or on the resource's organization
        // (scoped role, XD01-326). Holding it in neither place, no grant can help — skip the lookup.
        var holdsOnResourceOrganization = authContext.HasPermissionOn(resourceOrganizationId, permissionCode);
        if (userOrganizationId is null || (!hasHomePermission && !holdsOnResourceOrganization))
            return OrganizationResourceScope.None;

        var grants = await grantRepository.GetActiveGrantsAsync(userOrganizationId.Value, resourceOrganizationId, ct).ConfigureAwait(false);

        var qualifying = grants.Where(g =>
            OrganizationGrantMatching.CoversResourceType(g.ResourceType, resourceType) &&
            g.OrderTypeIds.Count == 0 &&
            OrganizationGrantMatching.AllowsAction(g.AllowedActions, permissionCode) &&
            (g.RequiresScopedRole
                ? holdsOnResourceOrganization && (g.RequiredRole is null || authContext.HasRoleOn(resourceOrganizationId, g.RequiredRole))
                : hasHomePermission && (g.RequiredRole is null || authContext.HasRole(g.RequiredRole)))).ToList();

        if (qualifying.Count == 0) return OrganizationResourceScope.None;
        if (qualifying.Any(g => g.ScopeId is null)) return OrganizationResourceScope.All;
        return OrganizationResourceScope.Of(qualifying.Select(g => g.ScopeId!));
    }
}
