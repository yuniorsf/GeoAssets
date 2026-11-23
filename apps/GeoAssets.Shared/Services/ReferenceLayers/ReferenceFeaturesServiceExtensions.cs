using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services.ReferenceLayers;

/// <summary>Registration of the REST client for another organization's reference features (XD01-331).</summary>
public static class ReferenceFeaturesServiceExtensions
{
    /// <summary>
    /// Registers <see cref="RestReferenceFeatureClient"/> as <see cref="IReferenceFeatureClient"/> over the host's
    /// <paramref name="httpClientName"/> named client (the one carrying the server's bearer token), based at
    /// <paramref name="geoAssetsServerBaseUrl"/><c>/api/geoassets/</c> — the same origin and client the other feature
    /// routes use. Replaces the <see cref="UnavailableReferenceFeatureClient"/> fallback of
    /// <see cref="PanelStateServiceExtensions.AddGeoAssetsPanelState"/>, in either order.
    /// </summary>
    public static IServiceCollection AddReferenceFeaturesRest(
        this IServiceCollection services, string geoAssetsServerBaseUrl, string httpClientName = "GeoAssetsServer")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(geoAssetsServerBaseUrl);

        var baseAddress = new Uri(geoAssetsServerBaseUrl.TrimEnd('/') + "/api/geoassets/");
        services.AddScoped<IReferenceFeatureClient>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);
            client.BaseAddress = baseAddress;
            return new RestReferenceFeatureClient(client);
        });
        return services;
    }
}
