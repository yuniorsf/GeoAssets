using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>DI registration for the guarded field-work transition client (XD01-295).</summary>
public static class FieldWorkTransitionServiceExtensions
{
    /// <summary>
    /// Registers <see cref="RestFieldWorkTransitionClient"/> pointed at <paramref name="baseUrl"/>
    /// (e.g. <c>http://localhost:5090/api/field-service/orders</c>). Uses the already-registered
    /// <c>"GeoAssetsServer"</c>-named <see cref="HttpClient"/> if the host configured one with an
    /// auth handler attached, otherwise falls back to an anonymous client — same convention as
    /// <see cref="GeoAssets.Shared.Services.Offline.OfflineSyncServiceExtensions.AddFieldServiceOfflineSync"/>.
    /// </summary>
    public static IServiceCollection AddFieldWorkTransitionClient(this IServiceCollection services, string baseUrl)
    {
        services.AddHttpClient();

        services.AddScoped<IFieldWorkTransitionClient>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            return new RestFieldWorkTransitionClient(client);
        });

        return services;
    }
}
