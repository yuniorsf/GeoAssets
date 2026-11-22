using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
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
/// Evaluation: (<see cref="OrgResourceRequirement.PermissionCode"/> passes AND the caller's
/// organization equals the resource's <c>OrganizationId</c>) OR a matching active
/// <c>OrganizationGrant</c> exists that the caller qualifies for (see below). A resource with <see cref="Guid.Empty"/>
/// <c>OrganizationId</c> (the "no organization assigned" sentinel — see
/// <see cref="IOrgOwnedResource"/>) is treated as unowned and always passes the org check:
/// this keeps every feature/asset-type created before XD01-20 (all defaulted to
/// <c>Guid.Empty</c>) accessible exactly as before, rather than mass-locking out existing
/// data the moment this handler ships.
///
/// A grant may additionally carry a <c>ScopeId</c>, narrowing it to one named subset of
/// <c>ResourceType</c> instead of all of it (e.g. one município's worth of a Distribuidora's
/// resources). When set, it only matches a resource that implements
/// <see cref="IScopeableResource"/> with an equal <c>ScopeId</c> — a resource type that doesn't
/// implement it can never satisfy a scoped grant. Null <c>ScopeId</c> (every grant predating
/// this field) is ungated, exactly as before (XD01-174).
///
/// How the caller qualifies for a grant depends on its <c>RequiresScopedRole</c> (XD01-326). False
/// (every grant predating the field): by holding <see cref="OrgResourceRequirement.PermissionCode"/>
/// at home, with <c>RequiredRole</c> matched against the home roles — exactly as before. True: only by
/// holding it through a role assigned on the resource's organization
/// (<see cref="AuthorizationContext.HasPermissionOn"/>), with <c>RequiredRole</c> matched against the
/// roles held there; home roles and permissions count for nothing. A grant narrowed to order types
/// (<c>OrderTypeIds</c>) only ever matches a service order, so it never matches a resource here.
/// </summary>
public sealed class OrgResourceAuthorizationHandler(
    IGeoAuthorizationService authorizationService,
    IOrganizationGrantRepository grantRepository)
    : AuthorizationHandler<OrgResourceRequirement, IOrgOwnedResource>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OrgResourceRequirement requirement, IOrgOwnedResource resource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(resource);
        // Same reasoning as GeoAuthorizationHandler: this handler can run even when the
        // route-level RequireAuthorization requirement hasn't succeeded yet, and
        // IGeoAuthorizationService throws for an anonymous caller rather than returning false.
        if (context.User.Identity?.IsAuthenticated != true) return;

        var hasHomePermission = await authorizationService.HasPermissionAsync(requirement.PermissionCode).ConfigureAwait(false);

        if (resource.OrganizationId == Guid.Empty)
        {
            if (hasHomePermission) context.Succeed(requirement);
            return;
        }

        var authContext = await authorizationService.GetAuthorizationContextAsync().ConfigureAwait(false);
        var userOrganizationId = authContext.User.OrganizationId;

        if (userOrganizationId == resource.OrganizationId)
        {
            if (hasHomePermission) context.Succeed(requirement);
            return;
        }

        // Every grant needs the permission somewhere: at home (organization-wide) or on the resource's organization
        // (scoped role, XD01-326). Holding it in neither place, no grant can help — skip the lookup.
        var holdsOnResourceOrganization = authContext.HasPermissionOn(resource.OrganizationId, requirement.PermissionCode);
        if (userOrganizationId is null || (!hasHomePermission && !holdsOnResourceOrganization))
            return;

        var resourceType = resource.GetType().Name;
        var grants = await grantRepository.GetActiveGrantsAsync(userOrganizationId.Value, resource.OrganizationId).ConfigureAwait(false);

        var hasGrant = grants.Any(g =>
            OrganizationGrantMatching.CoversResourceType(g.ResourceType, resourceType) &&
            g.OrderTypeIds.Count == 0 &&
            (g.ScopeId is null || (resource is IScopeableResource scoped && OrganizationGrantMatching.CoversScope(g.ScopeId, scoped.ScopeId))) &&
            OrganizationGrantMatching.AllowsAction(g.AllowedActions, requirement.PermissionCode) &&
            (g.RequiresScopedRole
                ? holdsOnResourceOrganization && (g.RequiredRole is null || authContext.HasRoleOn(resource.OrganizationId, g.RequiredRole))
                : hasHomePermission && (g.RequiredRole is null || authContext.HasRole(g.RequiredRole))));

        if (hasGrant)
            context.Succeed(requirement);
    }
}
