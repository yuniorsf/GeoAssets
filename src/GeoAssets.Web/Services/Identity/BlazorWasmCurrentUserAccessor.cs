using System.Security.Claims;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Components.Authorization;

namespace GeoAssets.Web.Services.Identity;

/// <summary>
/// Blazor WebAssembly implementation of <see cref="ICurrentUserAccessor"/>.
///
/// Reads the current user from <see cref="AuthenticationStateProvider"/>,
/// which is backed by MSAL (Azure AD) in the browser.
///
/// <see cref="GetCurrentUser"/> returns the last cached value (safe for sync callers).
/// <see cref="GetCurrentUserAsync"/> always refreshes from the live auth state.
/// </summary>
public sealed class BlazorWasmCurrentUserAccessor(
    AuthenticationStateProvider authStateProvider) : ICurrentUserAccessor
{
    private static readonly string OidClaim     = "oid";
    private static readonly string OidAltClaim  = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private static readonly string EmailClaim   = "preferred_username";
    private static readonly string EmailAlt     = "upn";
    private static readonly string NameClaim    = "name";
    private static readonly string RolesClaim   = "roles";

    private CurrentUser? _cached;

    /// <summary>Returns the last authenticated user resolved by <see cref="GetCurrentUserAsync"/>.</summary>
    public CurrentUser? GetCurrentUser() => _cached;

    /// <summary>Resolves the current user from the live Blazor authentication state.</summary>
    public async Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var state = await authStateProvider.GetAuthenticationStateAsync();
        _cached = Build(state.User);
        return _cached;
    }

    private static CurrentUser? Build(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return null;

        var oid = principal.FindFirst(OidClaim)?.Value
               ?? principal.FindFirst(OidAltClaim)?.Value
               ?? string.Empty;

        var email = principal.FindFirst(EmailClaim)?.Value
                 ?? principal.FindFirst(EmailAlt)?.Value
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
