using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Satisfies <see cref="GeoPolicyRequirement"/> by evaluating its name against
/// <see cref="IGeoAuthorizationService"/> (XD01-13) — the same engine the Blazor client
/// already uses. The name is either:
/// <list type="bullet">
///   <item>a raw <c>AppPermission.Code</c> (always <c>resource:action</c>, e.g.
///     <c>"features:read"</c>) — evaluated via <c>HasPermissionAsync</c>, for endpoints that
///     map directly to one permission and have no dedicated named policy (XD01-15).</item>
///   <item>an <c>AppPolicy.Name</c> (always PascalCase, e.g. <c>"CanEditFeatures"</c>) —
///     evaluated via <c>EvaluatePolicyAsync</c>, for endpoints that need a composed
///     Role/Claim/Permission check.</item>
/// </list>
/// The two are distinguished by the presence of <c>:</c>, matching the naming convention
/// already used consistently by every seeded permission/policy (see
/// <c>GeoIdentitySeeder</c>/<c>IdentitySeeder</c>).
///
/// Fails closed: an unknown policy name (typo, or a policy since deleted from the
/// database) makes <c>EvaluatePolicyAsync</c> throw <see cref="KeyNotFoundException"/>;
/// that's caught here and treated as "requirement not met" rather than propagating as an
/// unhandled 500. An unknown permission code needs no such handling — <c>HasPermissionAsync</c>
/// just returns false for any code the caller doesn't hold, known or not.
/// </summary>
public sealed class GeoAuthorizationHandler(
    IGeoAuthorizationService authorizationService,
    ILogger<GeoAuthorizationHandler> logger) : AuthorizationHandler<GeoPolicyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, GeoPolicyRequirement requirement)
    {
        // GeoAuthorizationPolicyProvider pairs every named policy with the framework's own
        // RequireAuthenticatedUser() requirement — but ASP.NET Core runs every requirement's
        // handler regardless of whether an earlier one already failed, so an anonymous caller
        // still reaches here. IGeoAuthorizationService assumes an authenticated context and
        // throws otherwise; deny cleanly instead of letting that propagate as an unhandled 500.
        if (context.User.Identity?.IsAuthenticated != true) return;

        var isPermissionCode = requirement.PolicyName.Contains(':');

        bool satisfied;
        try
        {
            satisfied = isPermissionCode
                ? await authorizationService.HasPermissionAsync(requirement.PolicyName)
                : await authorizationService.EvaluatePolicyAsync(requirement.PolicyName);
        }
        catch (KeyNotFoundException)
        {
            logger.LogWarning(
                "Authorization policy '{PolicyName}' has no matching AppPolicy row — denying by default.",
                requirement.PolicyName);
            return;
        }

        if (satisfied)
            context.Succeed(requirement);
    }
}
