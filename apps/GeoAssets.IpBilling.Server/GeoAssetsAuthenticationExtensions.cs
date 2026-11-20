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
/// This is the authenticated-caller bar every endpoint gets. The <c>ip-billing:*</c> permission
/// codes an endpoint names with <c>RequireAuthorization("…")</c> (the município grant endpoints,
/// XD01-385) are evaluated by <c>AddGeoPermissionPolicies</c> against the kernel's answer for the
/// caller's token (<c>AddKernelIdentity</c>) — this host has no identity database of its own; see
/// <c>IpBillingRestApiExtensions</c>'s doc comment for the endpoints that keep only this bar.
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
