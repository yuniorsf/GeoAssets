using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace GeoAssets.Server;

/// <summary>
/// Wires bearer-token authentication for <c>GeoAssets.Server</c> against the GeoAssets
/// Entra External ID (CIAM) tenant — see the <c>"AzureAdCiam"</c> section in
/// appsettings.json (<c>Instance</c>/<c>TenantId</c>/<c>ClientId</c>).
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
        IConfiguration          configuration)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAdCiam"));

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
