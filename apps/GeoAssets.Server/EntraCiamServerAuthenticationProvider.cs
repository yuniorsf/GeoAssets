using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace GeoAssets.Server;

/// <summary>
/// Default <see cref="IGeoAuthenticationProvider"/> (XD01-48): bearer-token validation against
/// the GeoAssets Entra External ID (CIAM) tenant, reading the <c>"AzureAdCiam"</c> section in
/// appsettings.json (<c>Instance</c>/<c>TenantId</c>/<c>ClientId</c>) — the same behavior
/// <see cref="GeoAssetsAuthenticationExtensions.AddGeoAssetsAuthentication"/> called directly
/// before this ticket. Internal: hosts depend on <see cref="IGeoAuthenticationProvider"/>, not
/// this concrete type — swapping CIAM vendors means passing a different implementation to
/// <see cref="GeoAssetsAuthenticationExtensions.AddGeoAssetsAuthentication"/>, not editing this
/// class.
/// </summary>
internal sealed class EntraCiamServerAuthenticationProvider : IGeoAuthenticationProvider
{
    public void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAdCiam"));
    }
}
