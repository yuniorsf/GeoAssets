using System.Security.Claims;

namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Reads the current user from a <see cref="ClaimsPrincipal"/>.
///
/// Works with any OIDC / OAuth 2.0 middleware that populates a ClaimsPrincipal
/// (Azure AD via Microsoft.Identity.Web, OpenIdConnect, JwtBearer, etc.) — claim-type
/// mapping is delegated to <see cref="ClaimMapping"/> (XD01-48) rather than hardcoded here,
/// so a different IdP's claim shape is a configuration change, not a code change.
///
/// Register in ASP.NET Core / Blazor:
/// <code>
///   builder.Services.AddHttpContextAccessor();
///   builder.Services.AddScoped&lt;ICurrentUserAccessor&gt;(sp =>
///       new ClaimsPrincipalCurrentUserAccessor(
///           () => sp.GetRequiredService&lt;IHttpContextAccessor&gt;()
///                   .HttpContext?.User));
/// </code>
/// </summary>
public sealed class ClaimsPrincipalCurrentUserAccessor(
    Func<ClaimsPrincipal?> principalResolver, ClaimMapping? claimMapping = null)
    : ICurrentUserAccessor
{
    private readonly ClaimMapping _claimMapping = claimMapping ?? ClaimMapping.EntraDefault;

    public CurrentUser? GetCurrentUser() => _claimMapping.Map(principalResolver());
}
