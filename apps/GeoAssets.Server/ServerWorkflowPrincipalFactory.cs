using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Server;

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
/// <see cref="IOrganizationGrantRepository"/> — the Blazor client's own
/// <c>WorkflowPrincipalFactory</c> does not (no REST endpoint exposes a caller's grants yet),
/// so cross-org <c>ServiceOrder</c> access via <c>CrossOrgGrantRule</c> is server-enforced only
/// for now, same scope boundary as XD01-16 itself.
/// </summary>
public sealed class ServerWorkflowPrincipalFactory(
    IGeoAuthorizationService authorizationService,
    IOrganizationGrantRepository grantRepository)
{
    /// <summary>
    /// <see cref="WorkflowPrincipal.GroupIds"/> is always empty — <see cref="AuthorizationContext"/>
    /// has no group source without a separate group-repository lookup. This only affects
    /// org/group-targeted dispatch rules, not the Creator/Assignee/Role rules used elsewhere.
    /// </summary>
    public async Task<WorkflowPrincipal> CreateAsync(CancellationToken ct = default)
    {
        var context = await authorizationService.GetAuthorizationContextAsync(ct);

        var orgGrants = context.User.OrganizationId is { } organizationId
            ? await grantRepository.GetActiveGrantsForGranteeAsync(organizationId, ct)
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
                RequiredRole: g.RequiredRole))],
        };
    }
}
