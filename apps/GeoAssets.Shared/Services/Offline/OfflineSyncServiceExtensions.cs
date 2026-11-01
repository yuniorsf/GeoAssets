using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// DI registration for the sync-client/gateway half of the offline scaffolding (XD01-243).
/// Deliberately does NOT register <see cref="ILocalServiceOrderStore"/>/<see cref="ILocalActionOutbox"/>/
/// <see cref="ILocalSyncCursorStore"/> — those are SQLCipher-backed and platform-specific, so the
/// host (<c>GeoAssets.MAUI</c>) registers them itself before calling this, the same split
/// <c>WorkflowRestServiceExtensions.AddWorkflowRest</c> uses for its own REST-vs-local pieces.
/// </summary>
public static class OfflineSyncServiceExtensions
{
    /// <summary>
    /// Registers <see cref="RestFieldServiceSyncClient"/> pointed at <paramref name="baseUrl"/>
    /// (e.g. <c>http://localhost:5090/api/field-service/sync</c>) and <see cref="MauiSyncGateway"/>
    /// on top of it. Uses the already-registered <c>"GeoAssetsServer"</c>-named
    /// <see cref="HttpClient"/> if the host configured one with an auth handler attached;
    /// otherwise falls back to an anonymous client.
    /// </summary>
    public static IServiceCollection AddFieldServiceOfflineSync(this IServiceCollection services, string baseUrl)
    {
        services.AddHttpClient();

        services.AddScoped<IFieldServiceSyncClient>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            return new RestFieldServiceSyncClient(client);
        });

        services.AddScoped<MauiSyncGateway>();

        return services;
    }
}
