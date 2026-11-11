using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

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
///
/// A caller holding the <see cref="ServiceCallerAppRole"/> app role (XD01-299) satisfies every
/// <see cref="GeoPolicyRequirement"/> without going through <c>IGeoAuthorizationService</c> at
/// all: that service answers "what can this *user* do", keyed off per-user permission rows, and
/// a service principal calling as itself (client-credentials, no human behind it) has none —
/// it's not a user to grant permissions to. Assigning this app role to a service principal in
/// its Entra app registration is therefore the whole authorization decision for that caller,
/// not a shortcut around one.
/// </summary>
public sealed partial class GeoAuthorizationHandler(
    IGeoAuthorizationService authorizationService,
    ILogger<GeoAuthorizationHandler> logger) : AuthorizationHandler<GeoPolicyRequirement>
{
    /// <summary>
    /// Entra app role that marks a caller as a trusted internal service (XD01-299) rather than a
    /// human/application user — assigned to a service principal's app registration, surfaced on
    /// its access token as a <c>roles</c> claim that ASP.NET Core's JWT bearer handler maps to
    /// <see cref="System.Security.Claims.ClaimTypes.Role"/> by default.
    /// </summary>
    public const string ServiceCallerAppRole = "GeoAssets.ServiceCaller";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, GeoPolicyRequirement requirement)
    {
        // GeoAuthorizationPolicyProvider pairs every named policy with the framework's own
        // RequireAuthenticatedUser() requirement — but ASP.NET Core runs every requirement's
        // handler regardless of whether an earlier one already failed, so an anonymous caller
        // still reaches here. IGeoAuthorizationService assumes an authenticated context and
        // throws otherwise; deny cleanly instead of letting that propagate as an unhandled 500.
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (context.User.Identity?.IsAuthenticated != true) return;

        if (context.User.IsInRole(ServiceCallerAppRole))
        {
            context.Succeed(requirement);
            return;
        }

        var isPermissionCode = requirement.PolicyName.Contains(':', StringComparison.Ordinal);

        bool satisfied;
        try
        {
            satisfied = isPermissionCode
                ? await authorizationService.HasPermissionAsync(requirement.PolicyName).ConfigureAwait(false)
                : await authorizationService.EvaluatePolicyAsync(requirement.PolicyName).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            LogUnknownPolicy(logger, requirement.PolicyName);
            return;
        }

        if (satisfied)
            context.Succeed(requirement);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authorization policy '{PolicyName}' has no matching AppPolicy row — denying by default.")]
    private static partial void LogUnknownPolicy(ILogger logger, string policyName);
}
