using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Default <see cref="IGeoAuthenticationProvider"/>: bearer-token validation against the
/// GeoAssets Entra External ID (CIAM) tenant, reading the <c>"AzureAdCiam"</c> section in
/// appsettings.json (<c>Instance</c>/<c>TenantId</c>/<c>ClientId</c>) — identical to
/// <c>GeoAssets.Server.EntraCiamServerAuthenticationProvider</c> (XD01-48); duplicated here
/// rather than shared, matching this codebase's existing per-host bootstrap convention (see
/// <c>GeoAssets.IpBilling.Server</c>, which has no equivalent of its own since it registers no
/// authentication at all). Internal: hosts depend on <see cref="IGeoAuthenticationProvider"/>,
/// not this concrete type.
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
