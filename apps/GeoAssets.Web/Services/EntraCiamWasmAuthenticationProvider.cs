using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace GeoAssets.Web.Services;

/// <summary>
/// Default <see cref="IGeoAuthenticationProvider"/> (XD01-48) for the Blazor WebAssembly
/// client: MSAL against the GeoAssets Entra External ID (CIAM) tenant, reading the
/// <c>"AzureAdCiam"</c> section in wwwroot/appsettings.json — the same behavior
/// <c>Program.cs</c> configured directly before this ticket. Internal: the composition root
/// depends on <see cref="IGeoAuthenticationProvider"/>, not this concrete type — swapping CIAM
/// vendors means passing a different implementation to
/// <see cref="GeoAssets.Web.Extensions.GeoAssetsWasmAuthenticationExtensions.AddGeoAssetsWasmAuthentication"/>,
/// not editing this class.
/// </summary>
internal sealed class EntraCiamWasmAuthenticationProvider : IGeoAuthenticationProvider
{
    public void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMsalAuthentication(options =>
        {
            configuration.Bind("AzureAdCiam", options.ProviderOptions.Authentication);
            // Use redirect instead of popup to avoid COOP (Cross-Origin-Opener-Policy)
            // browser restrictions that block window.closed monitoring in popup flow.
            options.ProviderOptions.LoginMode = "redirect";
        })
        // See RolesClaimsPrincipalFactory's doc comment — the default factory doesn't split
        // Entra's array-valued "roles" claim into individual claims.
        .AddAccountClaimsPrincipalFactory<RolesClaimsPrincipalFactory>();
    }
}
