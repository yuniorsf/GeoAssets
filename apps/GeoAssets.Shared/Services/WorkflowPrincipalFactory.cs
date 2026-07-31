using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Builds a <see cref="WorkflowPrincipal"/> for the currently authenticated user, bridging
/// <see cref="IGeoAuthorizationService"/> (Identity) into the Identity-independent Workflow module.
/// </summary>
public sealed class WorkflowPrincipalFactory(IGeoAuthorizationService authorizationService)
{
    /// <summary>
    /// <see cref="WorkflowPrincipal.GroupIds"/> is always empty — <see cref="AuthorizationContext"/>
    /// has no group source without a separate group-repository lookup. This only affects
    /// org/group-targeted dispatch rules, not the Creator/Assignee/Role rules used here.
    /// </summary>
    public async Task<WorkflowPrincipal> CreateAsync(CancellationToken ct = default)
    {
        var context = await authorizationService.GetAuthorizationContextAsync(ct);

        return new WorkflowPrincipal(
            UserId: context.User.Id.ToString(),
            OrganizationId: context.User.OrganizationId?.ToString(),
            RoleNames: context.Roles,
            GroupIds: [],
            PermissionCodes: context.Permissions);
    }
}
