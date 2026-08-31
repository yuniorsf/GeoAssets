using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// DI registration for the REST-backed <see cref="IServiceOrderRepository"/>/<see cref="IOrderTypeRepository"/>
/// (XD01-8) — the Postgres-backed alternative to <c>AddWorkflowInMemory</c> for hosts that talk to
/// <c>GeoAssets.Server</c> instead of holding state client-side.
/// </summary>
public static class WorkflowRestServiceExtensions
{
    /// <summary>
    /// Registers <see cref="RestServiceOrderRepository"/>/<see cref="RestOrderTypeRepository"/>
    /// pointed at <paramref name="baseUrl"/> (e.g. <c>http://localhost:5000/api/workflow</c> — the
    /// prefix <c>ServiceOrdersRestApiExtensions.MapServiceOrdersApi</c> mounts on the server).
    /// </summary>
    public static IServiceCollection AddWorkflowRest(this IServiceCollection services, string baseUrl)
    {
        services.AddHttpClient();

        services.AddSingleton(sp => new RestServiceOrderRepository(BuildClient(sp, baseUrl)));
        services.AddSingleton<IServiceOrderRepository>(sp => sp.GetRequiredService<RestServiceOrderRepository>());

        // Read-only or write-only consumers can depend on just the piece they need
        // instead of the full IServiceOrderRepository.
        services.AddSingleton<IServiceOrderReader>(sp => sp.GetRequiredService<IServiceOrderRepository>());
        services.AddSingleton<IServiceOrderWriter>(sp => sp.GetRequiredService<IServiceOrderRepository>());

        services.AddSingleton(sp => new RestOrderTypeRepository(BuildClient(sp, baseUrl)));
        services.AddSingleton<IOrderTypeRepository>(sp => sp.GetRequiredService<RestOrderTypeRepository>());

        return services;
    }

    private static HttpClient BuildClient(IServiceProvider sp, string baseUrl)
    {
        // Named (not the anonymous default) so a host's auth handler on this name also covers
        // ServiceOrders/OrderType calls — same reasoning as RestProviderFactory.BuildClient and
        // GeoIdentityRestExtensions.CreateIdentityClient. GeoAssets.Web attaches its CIAM
        // AuthorizationMessageHandler only to "GeoAssetsServer" (Program.cs, XD01-17); the
        // anonymous client this used to request never carried a token, so every call 401'd
        // against GeoAssets.Server's FallbackPolicy.RequireAuthenticatedUser() (XD01-127).
        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        return client;
    }
}
