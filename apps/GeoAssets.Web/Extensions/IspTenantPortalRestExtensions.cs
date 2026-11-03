using GeoAssets.Web.Services.Isp.Rest;
using GeoAssets.Workflow.Isp.NeutralOperator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// DI registration for XD01-269's tenant ISP self-service portal, backed by
/// <c>GeoAssets.Server</c>'s <c>/api/isp/tenant-portal/*</c> endpoints — reuses the same
/// "GeoAssetsServer" named HttpClient/bearer-token scope as <see cref="GeoIdentityRestExtensions"/>,
/// since both target the same host (unlike <see cref="IpBillingRestExtensions"/>, which targets
/// the separate <c>GeoAssets.IpBilling.Server</c> host).
/// </summary>
internal static class IspTenantPortalRestExtensions
{
    public static IServiceCollection AddIspTenantPortalRest(this IServiceCollection services)
    {
        services.AddScoped<ITenantPortalRestClient>(sp => new TenantPortalRestClient(CreateClient(sp)));
        return services;
    }

    private static HttpClient CreateClient(IServiceProvider sp)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["GeoAssetsServer:BaseUrl"]
            ?? throw new InvalidOperationException("GeoAssetsServer:BaseUrl is not configured.");

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/isp/tenant-portal/");
        return client;
    }
}
