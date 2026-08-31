using GeoAssets.Identity.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;

#if ANDROID
using Microsoft.Maui.ApplicationModel;
#endif

namespace GeoAssets.MAUI.Services.Identity;

/// <summary>
/// Default <see cref="IGeoAuthenticationProvider"/> (XD01-48/XD01-52) for GeoAssets.MAUI: MSAL.NET's
/// public-client interactive/silent flow against the same Entra External ID (CIAM) tenant as the
/// Web and Server hosts, reading the <c>"AzureAdCiamMaui"</c> section in appsettings.json. Unlike
/// <c>EntraCiamWasmAuthenticationProvider</c> (browser redirect) or
/// <c>EntraCiamServerAuthenticationProvider</c> (bearer-token validation), a native app performs
/// authentication itself via <see cref="IPublicClientApplication"/> rather than delegating to
/// ASP.NET Core/Blazor WASM authentication middleware — so this registers an
/// <see cref="IPublicClientApplication"/> singleton instead of calling an <c>AddXyz</c> auth
/// extension. <see cref="MauiMsalAuthenticationStateProvider"/> drives the actual sign-in/out
/// calls against it.
/// </summary>
internal sealed class EntraCiamMauiAuthenticationProvider : IGeoAuthenticationProvider
{
    public void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPublicClientApplication>(_ =>
        {
            var clientId  = configuration["AzureAdCiamMaui:ClientId"]  ?? string.Empty;
            var authority = configuration["AzureAdCiamMaui:Authority"] ?? string.Empty;

            var builder = PublicClientApplicationBuilder
                .Create(clientId)
                .WithAuthority(authority)
                // Computes the platform-correct redirect URI at runtime (Android:
                // "msal{ClientId}://auth", iOS/MacCatalyst: "msauth.{BundleId}://auth") instead
                // of hardcoding one scheme here — see the AzureAdCiamMaui comment in
                // appsettings.json for what must be registered in Azure to match it.
                .WithDefaultRedirectUri();

#if ANDROID
            // Required on Android: AcquireTokenInteractive needs a parent Activity to launch the
            // system-browser sign-in intent from.
            builder = builder.WithParentActivityOrWindow(() => Platform.CurrentActivity);
#endif

            return builder.Build();
        });
    }
}
