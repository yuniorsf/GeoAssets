using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// The token-carrying <c>"GeoAssetsIpBillingServer"</c> HttpClient (XD01-178), scoped to
/// <c>GeoAssetsIpBillingServer:BaseUrl</c> only. It requests <c>GeoAssetsIpBillingServer:ApiScope</c> — the IP-billing
/// API's own registration (CIAM model (b)) — or, while that is blank, the kernel's <c>GeoAssetsServer:ApiScope</c>, for an
/// IpBilling.Server that validates the kernel's audience (model (a), XD01-343).
/// </summary>
internal static class IpBillingServerClientExtensions
{
    public const string ClientName = "GeoAssetsIpBillingServer";

    public const string ApiScopeKey = "GeoAssetsIpBillingServer:ApiScope";

    private const string KernelApiScopeKey = "GeoAssetsServer:ApiScope";

    /// <summary>The IP-billing API's own scope, or <see langword="null"/> while it is blank.</summary>
    public static string? OwnApiScope(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var scope = configuration[ApiScopeKey];
        return string.IsNullOrWhiteSpace(scope) ? null : scope;
    }

    /// <summary>The scope the IP-billing client's token is requested for: its own, else the kernel's, else empty.</summary>
    public static string ResolveApiScope(IConfiguration configuration) =>
        OwnApiScope(configuration) ?? configuration[KernelApiScopeKey] ?? "";

    public static IHttpClientBuilder AddGeoAssetsIpBillingServerClient(
        this IServiceCollection services, IConfiguration configuration, string ipBillingServerBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipBillingServerBaseUrl);

        var scope = ResolveApiScope(configuration);
        return services.AddHttpClient(ClientName).AddHttpMessageHandler(sp =>
        {
            var handler = sp.GetRequiredService<AuthorizationMessageHandler>();
            handler.ConfigureHandler(authorizedUrls: [ipBillingServerBaseUrl], scopes: [scope]);
            return handler;
        });
    }
}
