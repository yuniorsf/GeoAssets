using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Wires bearer-token authentication for <c>GeoAssets.IpBilling.Server</c> — identical pattern
/// to <c>GeoAssets.Server.GeoAssetsAuthenticationExtensions</c>/
/// <c>GeoAssets.FieldService.Server.GeoAssetsAuthenticationExtensions</c> (XD01-48): defaults to
/// <see cref="EntraCiamServerAuthenticationProvider"/> unless a different
/// <see cref="IGeoAuthenticationProvider"/> is passed, and every endpoint requires an
/// authenticated caller by default via <see cref="AuthorizationOptions.FallbackPolicy"/>.
///
/// This is an authenticated-caller bar only — no fine-grained <c>ip-billing:*</c> permission-code
/// checks, matching <c>GeoAssets.FieldService.Server</c>'s own ceiling for this class of host
/// (neither has the EF-backed identity database <c>IGeoAuthorizationService</c> needs; see
/// <c>IpBillingRestApiExtensions</c>'s doc comment for the resulting scope decision).
/// </summary>
internal static class GeoAssetsAuthenticationExtensions
{
    public static IServiceCollection AddGeoAssetsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
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
