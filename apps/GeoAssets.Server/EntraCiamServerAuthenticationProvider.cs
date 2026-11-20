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
/// <para>
/// <c>AzureAdCiam:AdditionalAudiences</c> (XD01-390) widens the accepted audiences to the vertical hosts' API
/// registrations (<see cref="KernelAudiences"/>). The list is set as <c>TokenValidationParameters.ValidAudiences</c> in
/// the JwtBearer-options delegate, which Microsoft.Identity.Web runs before its own configuration of the same options —
/// that one registers its audience validator only while no <c>ValidAudiences</c> is set, so the list takes effect (a
/// later <c>Configure</c> would be too late: the validator, once registered, ignores <c>ValidAudiences</c>).
/// </para>
/// </summary>
internal sealed class EntraCiamServerAuthenticationProvider : IGeoAuthenticationProvider
{
    private const string SectionName = "AzureAdCiam";

    public void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(
                jwtBearer =>
                {
                    section.Bind(jwtBearer);
                    var additional = section.GetSection(KernelAudiences.AdditionalAudiencesKey).Get<string?[]>();
                    if (KernelAudiences.AnyAdditional(additional))
                        jwtBearer.TokenValidationParameters.ValidAudiences = KernelAudiences.Resolve(section["ClientId"], additional);
                },
                identity => section.Bind(identity));
    }
}
