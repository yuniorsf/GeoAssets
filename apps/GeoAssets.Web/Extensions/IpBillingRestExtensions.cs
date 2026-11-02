using GeoAssets.IpBilling.Repositories;
using GeoAssets.Web.Services.IpBilling.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// DI registration for the GEOS_FAT_IP reference-data admin UI (XD01-178), backed by
/// <c>GeoAssets.IpBilling.Server</c>'s <c>/api/ip-billing/*</c> endpoints — a separate host
/// from <c>GeoAssets.Server</c> (XD01-177), so it needs its own named HttpClient/bearer-token
/// scope rather than reusing <see cref="GeoIdentityRestExtensions"/>'s "GeoAssetsServer"
/// client. See Program.cs's "GeoAssetsIpBillingServer" HttpClient registration and
/// <c>EntraCiamWasmAuthenticationProvider</c>'s matching <c>DefaultAccessTokenScopes</c> entry.
///
/// Only <see cref="IMunicipioRepository"/> and <see cref="ILampTypeRepository"/> are registered
/// — the two reference-data surfaces XD01-178's admin pages (Municípios, Lamp Reference)
/// consume. <c>IUnidadeOrganizacionalRepository</c> has no admin page yet (out of scope for
/// this ticket) and is intentionally not registered here.
/// </summary>
internal static class IpBillingRestExtensions
{
    public static IServiceCollection AddIpBillingRest(this IServiceCollection services)
    {
        services.AddScoped<IMunicipioRepository>(sp =>
            new RestMunicipioRepository(CreateIpBillingClient(sp)));

        services.AddScoped<ILampTypeRepository>(sp =>
            new RestLampTypeRepository(CreateIpBillingClient(sp)));

        return services;
    }

    private static HttpClient CreateIpBillingClient(IServiceProvider sp)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["GeoAssetsIpBillingServer:BaseUrl"]
            ?? throw new InvalidOperationException("GeoAssetsIpBillingServer:BaseUrl is not configured.");

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsIpBillingServer");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/ip-billing/");
        return client;
    }
}
