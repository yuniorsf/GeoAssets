using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Default <see cref="IGeoAuthenticationProvider"/>: bearer-token validation against the GeoAssets Entra External ID (CIAM)
/// tenant, reading the <c>"AzureAdCiam"</c> section (<c>Instance</c>/<c>TenantId</c>/<c>ClientId</c>) — the same as the
/// kernel's and <c>GeoAssets.FieldService.Server</c>'s (XD01-48), duplicated per the per-host bootstrap convention. This
/// host forwards the caller's own token to the kernel's identity routes to build the caller's <c>WorkflowPrincipal</c>
/// (see <see cref="KernelIdentityServiceExtensions"/>), so the kernel must accept its audience (XD01-390): the
/// <c>ClientId</c> is this host's own API registration, listed in the kernel's <c>AzureAdCiam:AdditionalAudiences</c> —
/// or, alternatively, the kernel's API audience. A kernel that rejects the token answers 502 <c>kernel.token-rejected</c>.
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
