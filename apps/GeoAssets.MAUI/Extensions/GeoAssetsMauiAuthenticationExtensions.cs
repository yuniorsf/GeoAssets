using GeoAssets.Identity.Authentication;
using GeoAssets.MAUI.Services.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.MAUI.Extensions;

/// <summary>
/// Registers GeoAssets.MAUI's native authentication via an <see cref="IGeoAuthenticationProvider"/>
/// (XD01-48/XD01-52) — defaults to <see cref="EntraCiamMauiAuthenticationProvider"/> (MSAL.NET
/// public-client flow against the GeoAssets Entra External ID (CIAM) tenant) unless a different
/// provider is passed, mirroring <c>GeoAssetsWasmAuthenticationExtensions</c> (Web) and
/// <c>GeoAssetsAuthenticationExtensions</c> (Server): swapping CIAM vendors is a DI argument here
/// too, not a call to a vendor SDK at the composition root.
/// </summary>
public static class GeoAssetsMauiAuthenticationExtensions
{
    public static IServiceCollection AddGeoAssetsMauiAuthentication(
        this IServiceCollection services,
        IConfiguration          configuration,
        IGeoAuthenticationProvider? authenticationProvider = null)
    {
        (authenticationProvider ?? new EntraCiamMauiAuthenticationProvider())
            .AddAuthentication(services, configuration);

        return services;
    }
}
