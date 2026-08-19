using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// DI registration for the GeoAssets identity stack backed by <c>GeoAssets.Server</c>'s
/// read-only <c>/api/identity/*</c> endpoints (XD01-18) — the production alternative to
/// <see cref="GeoIdentityWasmExtensions.AddGeoIdentityWasmDev"/>'s in-memory store.
///
/// Requires <c>"GeoAssetsServer:BaseUrl"</c> configured and the <c>"GeoAssetsServer"</c>
/// named HttpClient registered with its MSAL <c>AuthorizationMessageHandler</c> (both from
/// Program.cs, XD01-17) so requests carry a bearer token the server will accept.
///
/// Does not register <c>UserProvisioningService</c> or the granular
/// <c>IUserRepository</c>/<c>IRoleRepository</c>/etc. repositories — nothing outside the
/// identity module consumes those directly (only <see cref="IGeoAuthorizationService"/> is
/// used app-wide, e.g. by <c>WorkflowPrincipalFactory</c>), and JIT user provisioning
/// against a read-only API isn't possible — that's tracked separately (see XD01-12's
/// federated-auth/JIT-provisioning follow-up note).
/// </summary>
public static class GeoIdentityRestExtensions
{
    public static IServiceCollection AddGeoIdentityRest(this IServiceCollection services)
    {
        services.AddScoped<IGeoAuthorizationService>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["GeoAssetsServer:BaseUrl"]
                ?? throw new InvalidOperationException(
                    "Identity:Backend is 'Rest' but GeoAssetsServer:BaseUrl is not configured.");

            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/identity/");
            return new RestGeoAuthorizationService(client);
        });

        return services;
    }
}
