using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Wires bearer-token authentication for <c>GeoAssets.FieldService.Server</c> — identical
/// pattern to <c>GeoAssets.Server.GeoAssetsAuthenticationExtensions</c> (XD01-48): defaults to
/// <see cref="EntraCiamServerAuthenticationProvider"/> unless a different
/// <see cref="IGeoAuthenticationProvider"/> is passed, and every endpoint requires an
/// authenticated caller by default via <see cref="AuthorizationOptions.FallbackPolicy"/>.
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
