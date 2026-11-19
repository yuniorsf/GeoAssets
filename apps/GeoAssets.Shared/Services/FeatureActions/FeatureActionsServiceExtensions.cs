using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services.FeatureActions;

/// <summary>Registration of the map's feature actions (XD01-370).</summary>
public static class FeatureActionsServiceExtensions
{
    /// <summary>
    /// Registers <see cref="RestFeatureActionsClient"/> as <see cref="IFeatureActionsClient"/> over the host's
    /// <paramref name="httpClientName"/> named client (the one carrying the server's bearer token), based at
    /// <paramref name="geoAssetsServerBaseUrl"/><c>/api/geoassets/</c>.
    /// </summary>
    public static IServiceCollection AddFeatureActionsRest(
        this IServiceCollection services, string geoAssetsServerBaseUrl, string httpClientName = "GeoAssetsServer")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(geoAssetsServerBaseUrl);

        var baseAddress = new Uri(geoAssetsServerBaseUrl.TrimEnd('/') + "/api/geoassets/");
        services.AddScoped<IFeatureActionsClient>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);
            client.BaseAddress = baseAddress;
            return new RestFeatureActionsClient(client);
        });
        return services;
    }
}
