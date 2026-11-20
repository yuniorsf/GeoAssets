using GeoAssets.Identity.Authentication;
using GeoAssets.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// The token-carrying <c>"GeoAssetsServer"</c> HttpClient every Rest-backed domain requests (XD01-17), scoped to the
/// configured, trusted gateway origin only — never to arbitrary REST URLs a user might type into the provider-pool UI
/// (RestProviderPlugin). With no <c>GeoAssetsServer:RouteScopes</c> entry configured, the framework
/// <see cref="AuthorizationMessageHandler"/> attaches a <c>GeoAssetsServer:ApiScope</c> token to every request, as
/// before; with one, <see cref="RouteScopedAuthorizationMessageHandler"/> attaches each route's own scope (XD01-397).
/// </summary>
internal static class GeoAssetsServerClientExtensions
{
    public const string ClientName = "GeoAssetsServer";

    private const string SectionName = "GeoAssetsServer";

    public static IHttpClientBuilder AddGeoAssetsServerClient(
        this IServiceCollection services, IConfiguration configuration, string geoAssetsServerBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(geoAssetsServerBaseUrl);

        var section = configuration.GetSection(SectionName);
        var routes = ApiScopeRoutes.FromConfiguration(section, new Uri(geoAssetsServerBaseUrl, UriKind.Absolute));
        services.AddSingleton(routes);

        var client = services.AddHttpClient(ClientName);
        if (!routes.HasRouteScopes)
        {
            return client.AddHttpMessageHandler(sp =>
            {
                var handler = sp.GetRequiredService<AuthorizationMessageHandler>();
                handler.ConfigureHandler(
                    authorizedUrls: [geoAssetsServerBaseUrl],
                    scopes: [section[ApiScopeRoutes.ApiScopeKey] ?? ""]);
                return handler;
            });
        }

        return client.AddHttpMessageHandler(sp => new RouteScopedAuthorizationMessageHandler(
            sp.GetRequiredService<IAccessTokenProvider>(),
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<ApiScopeRoutes>()));
    }
}
