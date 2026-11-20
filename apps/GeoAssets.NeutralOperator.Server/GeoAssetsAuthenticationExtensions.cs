using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Wires bearer-token authentication for <c>GeoAssets.NeutralOperator.Server</c> — the kernel's pattern (XD01-48): defaults
/// to <see cref="EntraCiamServerAuthenticationProvider"/> unless a different <see cref="IGeoAuthenticationProvider"/> is
/// passed, and every endpoint requires an authenticated caller by default (<see cref="AuthorizationOptions.FallbackPolicy"/>).
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
