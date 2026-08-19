using GeoAssets.Identity.Authentication;
using GeoAssets.Web.Services;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// Registers Blazor WebAssembly authentication via an <see cref="IGeoAuthenticationProvider"/>
/// (XD01-48) — defaults to <see cref="EntraCiamWasmAuthenticationProvider"/> (MSAL against the
/// GeoAssets Entra External ID (CIAM) tenant) unless a different provider is passed, so
/// <c>Program.cs</c> no longer calls <c>Microsoft.Authentication.WebAssembly.Msal</c>'s
/// <c>AddMsalAuthentication</c> directly — swapping CIAM vendors is then a DI argument, not a
/// call to a vendor SDK at the composition root.
/// </summary>
public static class GeoAssetsWasmAuthenticationExtensions
{
    public static IServiceCollection AddGeoAssetsWasmAuthentication(
        this IServiceCollection services,
        IConfiguration          configuration,
        IGeoAuthenticationProvider? authenticationProvider = null)
    {
        (authenticationProvider ?? new EntraCiamWasmAuthenticationProvider())
            .AddAuthentication(services, configuration);

        return services;
    }
}
