using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Builds a <see cref="WorkflowPrincipal"/> for the currently authenticated caller, bridging
/// <see cref="IGeoAuthorizationService"/> (Identity) into the Identity-independent Workflow
/// module — the server-side equivalent of <c>GeoAssets.Shared.Services.WorkflowPrincipalFactory</c>
/// (XD01-16). Duplicated rather than shared: <c>GeoAssets.Server</c> is a plain ASP.NET Core
/// host and doesn't reference <c>GeoAssets.Shared</c> (a Blazor Razor Class Library), and this
/// factory has no Razor/Blazor dependency of its own — the same reasoning as
/// <c>GeoIdentitySeeder</c>'s duplication from the WASM <c>IdentitySeeder</c> (XD01-14).
///
/// Also resolves <see cref="WorkflowPrincipal.OrgGrants"/> (XD01-22) from
/// <see cref="IOrganizationGrantRepository"/> — the active, unexpired grants held by the
/// caller's own organization. The Blazor client's <c>WorkflowPrincipalFactory</c> resolves the
/// same grants through <c>GET /api/identity/organization-grants/me</c> (XD01-332), so the
/// organization gate and <c>CrossOrgGrantRule</c> decide the same on both sides.
///
/// A host without the identity database (a vertical microservice, XD01-382) builds it over an
/// <see cref="IOwnOrganizationGrantSource"/> instead — the kernel's answer for the caller's own token.
///
/// The roles and permissions the caller holds on other organizations (XD01-325) are copied from
/// <see cref="AuthorizationContext.ScopedRoles"/>/<see cref="AuthorizationContext.ScopedPermissions"/>
/// into <see cref="WorkflowPrincipal.ScopedRoleNames"/>/<see cref="WorkflowPrincipal.ScopedPermissionCodes"/>,
/// so a grant with <see cref="WorkflowOrgGrant.RequiresScopedRole"/> is decided by them (XD01-326).
/// Wherever the context carries none, such a grant opens nothing.
/// </summary>
public sealed class ServerWorkflowPrincipalFactory
{
    private readonly IGeoAuthorizationService _authorizationService;
    private readonly Func<Guid, CancellationToken, Task<IReadOnlyList<OrganizationGrant>>> _activeGrantsForGrantee;

    /// <summary>The kernel's factory: grants from the identity database.</summary>
    public ServerWorkflowPrincipalFactory(IGeoAuthorizationService authorizationService, IOrganizationGrantRepository grantRepository)
    {
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(grantRepository);

        _authorizationService = authorizationService;
        _activeGrantsForGrantee = (organizationId, ct) => grantRepository.GetActiveGrantsForGranteeAsync(organizationId, ct);
    }

    /// <summary>A vertical host's factory (XD01-382): grants from <paramref name="grantSource"/>.</summary>
    public ServerWorkflowPrincipalFactory(IGeoAuthorizationService authorizationService, IOwnOrganizationGrantSource grantSource)
    {
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(grantSource);

        _authorizationService = authorizationService;
        _activeGrantsForGrantee = grantSource.GetActiveGrantsForGranteeAsync;
    }

    /// <summary>
    /// <see cref="WorkflowPrincipal.GroupIds"/> is always empty — <see cref="AuthorizationContext"/>
    /// has no group source without a separate group-repository lookup. This only affects
    /// org/group-targeted dispatch rules, not the Creator/Assignee/Role rules used elsewhere.
    /// </summary>
    public async Task<WorkflowPrincipal> CreateAsync(CancellationToken ct = default)
    {
        var context = await _authorizationService.GetAuthorizationContextAsync(ct).ConfigureAwait(false);

        var orgGrants = context.User.OrganizationId is { } organizationId
            ? await _activeGrantsForGrantee(organizationId, ct).ConfigureAwait(false)
            : [];

        return new WorkflowPrincipal(
            UserId: context.User.Id.ToString(),
            OrganizationId: context.User.OrganizationId?.ToString(),
            RoleNames: context.Roles,
            GroupIds: [],
            PermissionCodes: context.Permissions)
        {
            OrgGrants = [.. orgGrants.Select(g => new WorkflowOrgGrant(
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
