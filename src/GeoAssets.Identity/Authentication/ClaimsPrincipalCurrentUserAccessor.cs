using System.Security.Claims;

namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Reads the current user from a <see cref="ClaimsPrincipal"/>.
///
/// Works with any OIDC / OAuth 2.0 middleware that populates a ClaimsPrincipal
/// (Azure AD via Microsoft.Identity.Web, OpenIdConnect, JwtBearer, etc.).
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
public sealed class ClaimsPrincipalCurrentUserAccessor(Func<ClaimsPrincipal?> principalResolver)
    : ICurrentUserAccessor
{
    // Standard Azure AD claim types
    private const string OidClaim          = "oid";
    private const string OidAlternateClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string EmailClaim        = "preferred_username";
    private const string EmailAltClaim     = "upn";
    private const string NameClaim         = "name";
    private const string RolesClaim        = "roles";

    public CurrentUser? GetCurrentUser()
    {
        var principal = principalResolver();
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var oid = principal.FindFirst(OidClaim)?.Value
               ?? principal.FindFirst(OidAlternateClaim)?.Value
               ?? string.Empty;

        var email = principal.FindFirst(EmailClaim)?.Value
                 ?? principal.FindFirst(EmailAltClaim)?.Value
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value
                 ?? string.Empty;

        var displayName = principal.FindFirst(NameClaim)?.Value
                       ?? principal.FindFirst(ClaimTypes.Name)?.Value
                       ?? email;

        var roles = principal.FindAll(RolesClaim)
                             .Concat(principal.FindAll(ClaimTypes.Role))
                             .Select(c => c.Value)
                             .Distinct()
                             .ToList();

        return new CurrentUser(oid, email, displayName, roles);
    }
}
