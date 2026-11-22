using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Builds a <see cref="WorkflowPrincipal"/> for the currently authenticated user, bridging
/// <see cref="IGeoAuthorizationService"/> (Identity) into the Identity-independent Workflow module.
///
/// The principal carries everything the service-order organization gate (XD01-332) reads, so
/// <c>ServiceOrderRules</c> decides here what it decides on the server: the user's own
/// organization, and the active grants held by that organization
/// (<see cref="AuthorizationContext.OrganizationGrants"/>, which the REST-backed authorization
/// service fills from <c>GET /api/identity/organization-grants/me</c>). The third way through
/// the gate, being a dispatch recipient, is read from the order itself.
///
/// The Web client's authorization service fills the grants and the roles and permissions held on
/// other organizations (<see cref="AuthorizationContext.ScopedRoles"/>/<see cref="AuthorizationContext.ScopedPermissions"/>)
/// from <c>GET /api/identity/me/cross-org-entitlements</c> instead (XD01-329), so a grant that
/// requires a scoped role decides here as on the server. Build the principal again after
/// <see cref="ICrossOrgEntitlementsService.RefreshAsync"/> to pick up new entitlements.
/// </summary>
public sealed class WorkflowPrincipalFactory(IGeoAuthorizationService authorizationService)
{
    /// <summary>
    /// <see cref="WorkflowPrincipal.GroupIds"/> is always empty — <see cref="AuthorizationContext"/>
    /// has no group source without a separate group-repository lookup, on the server either
    /// (<c>ServerWorkflowPrincipalFactory</c>), so both sides agree that nobody is a recipient
    /// of a group-targeted dispatch.
    /// </summary>
    public async Task<WorkflowPrincipal> CreateAsync(CancellationToken ct = default)
    {
        var context = await authorizationService.GetAuthorizationContextAsync(ct);

        return new WorkflowPrincipal(
            UserId: context.User.Id.ToString(),
            OrganizationId: context.User.OrganizationId?.ToString(),
            RoleNames: context.Roles,
            GroupIds: [],
            PermissionCodes: context.Permissions)
        {
            OrgGrants = [.. context.OrganizationGrants.Select(g => new WorkflowOrgGrant(
                ResourceOrganizationId: g.ResourceOrganizationId.ToString(),
                ResourceType: g.ResourceType,
                AllowedActions: g.AllowedActions,
                RequiredRole: g.RequiredRole,
                ScopeId: g.ScopeId)
            {
                ExpiresAt = g.ExpiresAt,
                RequiresScopedRole = g.RequiresScopedRole,
                OrderTypeIds = g.OrderTypeIds,
            })],
            ScopedRoleNames = context.ScopedRoles,
            ScopedPermissionCodes = context.ScopedPermissions,
        };
    }
}
