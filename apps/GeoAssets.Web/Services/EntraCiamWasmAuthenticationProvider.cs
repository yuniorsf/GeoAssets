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
        // GeoAssetsServer:ApiScope is a public-repo placeholder ("api://YOUR_SERVER_API_
        // CLIENT_ID/access_as_user") until someone configures a real CIAM app for it — guard on
        // it actually being set rather than requesting a placeholder scope unconditionally,
        // which would break the interactive login itself.
        var serverApiScope = configuration["GeoAssetsServer:ApiScope"];

        // Same placeholder-guard reasoning as above, for GeoAssets.IpBilling.Server's own CIAM
        // app registration (XD01-178) — a separate audience from GeoAssetsServer:ApiScope.
        var ipBillingApiScope = configuration["GeoAssetsIpBillingServer:ApiScope"];

        // The GeoAssetsServer:RouteScopes scopes (XD01-397) — another resource's API each (e.g. the NeutralOperator
        // host's), whose token RouteScopedAuthorizationMessageHandler requests on its own. A token request carries only
        // one resource's scopes, so these are consented at sign-in rather than added to DefaultAccessTokenScopes.
        // Program.cs fails fast on a missing GeoAssetsServer:BaseUrl; here a missing one just asks for no extra consent.
        var serverBaseUrl = configuration["GeoAssetsServer:BaseUrl"];
        IReadOnlyList<string> routeScopes = string.IsNullOrWhiteSpace(serverBaseUrl)
            ? []
            : ApiScopeRoutes.FromConfiguration(
                configuration.GetSection("GeoAssetsServer"), new Uri(serverBaseUrl, UriKind.Absolute)).AdditionalScopes;

        services.AddMsalAuthentication(options =>
        {
            configuration.Bind("AzureAdCiam", options.ProviderOptions.Authentication);
            // Use redirect instead of popup to avoid COOP (Cross-Origin-Opener-Policy)
            // browser restrictions that block window.closed monitoring in popup flow.
            options.ProviderOptions.LoginMode = "redirect";

            // Request the GeoAssets Server API scope at interactive sign-in time. Without
            // this, the resulting refresh token never carries authorization for it, so
            // AuthorizationMessageHandler's later silent token request for that scope (see
            // Program.cs's "GeoAssetsServer" HttpClient registration) 400s against the CIAM
            // token endpoint on every call instead of ever succeeding — CIAM doesn't silently
            // expand a refresh token's scope via the refresh_token grant.
            if (!string.IsNullOrWhiteSpace(serverApiScope))
                options.ProviderOptions.DefaultAccessTokenScopes.Add(serverApiScope);

            // Same reasoning, for GeoAssets.IpBilling.Server's own API scope (XD01-178) — its
            // own CIAM app registration/audience, distinct from GeoAssetsServer's.
            if (!string.IsNullOrWhiteSpace(ipBillingApiScope))
                options.ProviderOptions.DefaultAccessTokenScopes.Add(ipBillingApiScope);

            foreach (var routeScope in routeScopes)
                options.ProviderOptions.AdditionalScopesToConsent.Add(routeScope);
        })
        // See RolesClaimsPrincipalFactory's doc comment — the default factory doesn't split
        // Entra's array-valued "roles" claim into individual claims.
        .AddAccountClaimsPrincipalFactory<RolesClaimsPrincipalFactory>();
    }
}
