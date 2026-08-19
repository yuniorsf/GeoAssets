using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Wires bearer-token authentication for <c>GeoAssets.Server</c> via an
/// <see cref="IGeoAuthenticationProvider"/> (XD01-48) — defaults to
/// <see cref="EntraCiamServerAuthenticationProvider"/> (the GeoAssets Entra External ID (CIAM)
/// tenant, <c>"AzureAdCiam"</c> section in appsettings.json:
/// <c>Instance</c>/<c>TenantId</c>/<c>ClientId</c>) unless a different provider is passed —
/// swapping CIAM vendors is then a DI argument, not a call to <c>Microsoft.Identity.Web</c>
/// (or any other vendor SDK) at this call site.
///
/// Every endpoint requires an authenticated caller by default (via
/// <see cref="AuthorizationOptions.FallbackPolicy"/>) — this closes the "wide open
/// behind CORS only" gap; opt an endpoint out with <c>[AllowAnonymous]</c>.
///
/// This validates <i>who</i> the caller is only. <i>What</i> they're allowed to do
/// (roles/permissions/policies) is a separate concern bridged from
/// <c>IGeoAuthorizationService</c>/<c>AppPolicy</c> (XD01-13), not implemented here.
/// </summary>
public static class GeoAssetsAuthenticationExtensions
{
    public static IServiceCollection AddGeoAssetsAuthentication(
        this IServiceCollection services,
        IConfiguration          configuration,
        IGeoAuthenticationProvider? authenticationProvider = null)
    {
        (authenticationProvider ?? new EntraCiamServerAuthenticationProvider())
            .AddAuthentication(services, configuration);

        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    public static IApplicationBuilder UseGeoAssetsAuthentication(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
