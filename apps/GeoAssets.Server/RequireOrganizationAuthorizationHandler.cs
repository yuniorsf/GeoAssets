using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Satisfies <see cref="RequireOrganizationRequirement"/> by checking
/// <see cref="IGeoAuthorizationService.GetAuthorizationContextAsync"/>'s resolved
/// <c>AuthorizationContext.User.OrganizationId</c> is non-null.
///
/// Deliberately a post-authentication, authorization-time check — never at JWT
/// OnTokenValidated time. GetAuthorizationContextAsync (XD01-88) itself performs
/// JIT provisioning when no AppUser row yet exists for the caller's ExternalObjectId,
/// leaving OrganizationId null on purpose; org assignment happens afterwards via the
/// admin UI (XD01-63), per XD01-49's resolution. Blocking at token-validation time
/// would prevent that very first provisioning call from ever running, so no admin
/// could assign an org to a user who was never allowed to log in. Running this as an
/// authorization requirement instead lets that first request still complete —
/// whatever endpoint it hit — and only denies requests made after that, for as long
/// as OrganizationId stays null.
/// </summary>
public sealed class RequireOrganizationAuthorizationHandler(
    IGeoAuthorizationService authorizationService) : AuthorizationHandler<RequireOrganizationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RequireOrganizationRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Same reasoning as GeoAuthorizationHandler/OrgResourceAuthorizationHandler: this
        // handler can run before a paired RequireAuthenticatedUser() requirement has
        // succeeded, and IGeoAuthorizationService throws for an anonymous caller rather
        // than returning false.
        if (context.User.Identity?.IsAuthenticated != true) return;

        var authContext = await authorizationService.GetAuthorizationContextAsync().ConfigureAwait(false);

        if (authContext.User.OrganizationId is not null)
            context.Succeed(requirement);
    }
}
