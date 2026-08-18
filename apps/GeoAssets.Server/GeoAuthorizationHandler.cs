using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Satisfies <see cref="GeoPolicyRequirement"/> by evaluating the named
/// <c>AppPolicy</c> via <see cref="IGeoAuthorizationService.EvaluatePolicyAsync(string, CancellationToken)"/>
/// (XD01-13) — the same policy engine the Blazor client already uses.
///
/// Fails closed: an unknown policy name (typo, or a policy since deleted from the
/// database) makes <c>EvaluatePolicyAsync</c> throw <see cref="KeyNotFoundException"/>;
/// that's caught here and treated as "requirement not met" rather than propagating as an
/// unhandled 500, so a bad policy name can never accidentally bypass authorization.
/// </summary>
public sealed class GeoAuthorizationHandler(
    IGeoAuthorizationService authorizationService,
    ILogger<GeoAuthorizationHandler> logger) : AuthorizationHandler<GeoPolicyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, GeoPolicyRequirement requirement)
    {
        bool satisfied;
        try
        {
            satisfied = await authorizationService.EvaluatePolicyAsync(requirement.PolicyName);
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
