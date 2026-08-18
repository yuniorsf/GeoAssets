using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// ASP.NET Core resource-based authorization handler (XD01-21) — the subject-only AuthZ
/// bridge (XD01-13/XD01-15) can answer "can this user do X" but has no notion of which
/// resource X targets. This handler answers "can this user do X *to this specific
/// org-owned resource*", invoked via <c>IAuthorizationService.AuthorizeAsync(user, resource,
/// requirement)</c> once the endpoint has actually loaded the resource — not through
/// <c>.RequireAuthorization</c> route middleware, which runs before any resource is loaded.
///
/// Evaluation: <see cref="OrgResourceRequirement.PermissionCode"/> passes AND (the caller's
/// organization equals the resource's <c>OrganizationId</c> OR a matching active
/// <c>OrganizationGrant</c> exists). A resource with <see cref="Guid.Empty"/>
/// <c>OrganizationId</c> (the "no organization assigned" sentinel — see
/// <see cref="IOrgOwnedResource"/>) is treated as unowned and always passes the org check:
/// this keeps every feature/asset-type created before XD01-20 (all defaulted to
/// <c>Guid.Empty</c>) accessible exactly as before, rather than mass-locking out existing
/// data the moment this handler ships.
/// </summary>
public sealed class OrgResourceAuthorizationHandler(
    IGeoAuthorizationService authorizationService,
    IOrganizationGrantRepository grantRepository)
    : AuthorizationHandler<OrgResourceRequirement, IOrgOwnedResource>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OrgResourceRequirement requirement, IOrgOwnedResource resource)
    {
        if (!await authorizationService.HasPermissionAsync(requirement.PermissionCode))
            return;

        if (resource.OrganizationId == Guid.Empty)
        {
            context.Succeed(requirement);
            return;
        }

        var authContext = await authorizationService.GetAuthorizationContextAsync();
        var userOrganizationId = authContext.User.OrganizationId;

        if (userOrganizationId == resource.OrganizationId)
        {
            context.Succeed(requirement);
            return;
        }

        if (userOrganizationId is null)
            return;

        var resourceType = resource.GetType().Name;
        var grants = await grantRepository.GetActiveGrantsAsync(userOrganizationId.Value, resource.OrganizationId);

        var hasGrant = grants.Any(g =>
            (g.ResourceType is null || g.ResourceType == resourceType) &&
            g.AllowedActions.Contains(requirement.PermissionCode) &&
            (g.RequiredRole is null || authContext.HasRole(g.RequiredRole)));

        if (hasGrant)
            context.Succeed(requirement);
    }
}
