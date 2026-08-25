using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace GeoAssets.Web.Services;

/// <summary>
/// Expands a JSON array-valued <c>"roles"</c> claim (Entra App Roles) into one claim per role.
///
/// <c>Microsoft.Authentication.WebAssembly.Msal</c>'s default <see cref="AccountClaimsPrincipalFactory{TAccount}"/>
/// keeps an array-valued token claim as a single claim whose <c>Value</c> is the raw JSON array
/// text (e.g. <c>["Administrator"]</c>, brackets and all) rather than one claim per array
/// element — a well-documented gap in the default factory that Microsoft's own Blazor WASM /
/// Entra docs cover overriding <c>AccountClaimsPrincipalFactory</c> for. Without this,
/// <see cref="GeoAssets.Identity.Authentication.ClaimMapping.Map"/> never matches a real role
/// name against the raw bracketed string, so every authorization check silently sees an empty
/// roles list regardless of the user's actual App Role assignment.
/// </summary>
internal sealed class RolesClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
    : AccountClaimsPrincipalFactory<RemoteUserAccount>(accessor)
{
    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
        RemoteUserAccount account, RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);

        if (user.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return user;

        foreach (var rolesClaim in identity.FindAll("roles").ToList())
        {
            if (!rolesClaim.Value.TrimStart().StartsWith('[')) continue;

            string[] roles;
            try
            {
                roles = JsonSerializer.Deserialize<string[]>(rolesClaim.Value) ?? [];
            }
            catch (JsonException)
            {
                continue; // not actually a JSON array — leave the claim as-is
            }

            identity.RemoveClaim(rolesClaim);
            foreach (var role in roles)
                identity.AddClaim(new Claim("roles", role));
        }

        return user;
    }
}
