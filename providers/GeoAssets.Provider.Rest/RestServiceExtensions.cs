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
        services.AddSingleton<IExternalProviderFactory>(
            sp => sp.GetRequiredService<RestProviderFactory>());
        return services;
    }
}
