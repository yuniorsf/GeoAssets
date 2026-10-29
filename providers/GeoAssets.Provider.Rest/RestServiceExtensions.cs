using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Rest;

public static class RestServiceExtensions
{
    /// <summary>
    /// Registers the REST provider factory so <c>ProviderPoolPanel</c> discovers it
    /// and shows a 🔌 REST connection button.
    ///
    /// On the server side, call <c>app.MapGeoAssetsApi()</c> (from
    /// <c>GeoAssets.Provider.PostgreSQL</c>) to expose the API endpoints.
    /// </summary>
    public static IServiceCollection AddGeoAssetsRest(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<RestProviderFactory>();
        // Keep legacy factory registration for any code that still resolves IExternalProviderFactory
        services.AddSingleton<IExternalProviderFactory>(
            sp => sp.GetRequiredService<RestProviderFactory>());
        // Plugin registration — discovered by ProviderPluginRegistry and the boot/pool UI
        services.AddSingleton<IProviderPlugin, RestProviderPlugin>();
        return services;
    }

    /// <summary>
    /// Registers the stateless <see cref="IGeoCoreClient"/> (<see cref="RestGeoCoreClient"/>)
    /// pointed at <paramref name="baseUrl"/> (e.g. <c>http://localhost:5000/api/geoassets</c>)
    /// for vertical microservices querying the Core GeoAssets Server.
    /// </summary>
    public static IServiceCollection AddGeoCoreRestClient(this IServiceCollection services, string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        services.AddHttpClient();

        services.AddSingleton<IGeoCoreClient>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            return new RestGeoCoreClient(client);
        });

        return services;
    }
}
