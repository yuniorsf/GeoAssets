using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// DI registration for the REST-backed <see cref="IServiceOrderRepository"/>/<see cref="IOrderTypeRepository"/>
/// (XD01-8) — <c>GeoAssets.Web</c>'s sole Service Order/OrderType registration (XD01-129), always
/// pointed at <c>GeoAssets.Server</c> instead of holding state client-side.
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

        // Service-order targets and selection (XD01-308) — same named client, same lifetime.
        services.AddSingleton(sp => new RestServiceOrderTargetRepository(BuildClient(sp, baseUrl)));
        services.AddSingleton<IServiceOrderTargetReader>(sp => sp.GetRequiredService<RestServiceOrderTargetRepository>());
        services.AddSingleton<IServiceOrderTargetWriter>(sp => sp.GetRequiredService<RestServiceOrderTargetRepository>());

        services.AddSingleton(sp => new RestOrderTargetSelectionService(BuildClient(sp, baseUrl)));
        services.AddSingleton<IOrderTargetSelectionService>(sp => sp.GetRequiredService<RestOrderTargetSelectionService>());

        services.AddSingleton(sp => new RestMapSelectionClient(BuildClient(sp, baseUrl)));

        // Staged occupancy changes (XD01-315) — same named client, same lifetime.
        services.AddSingleton(sp => new RestOrderOccupancyStagingService(BuildClient(sp, baseUrl)));
        services.AddSingleton<IOrderOccupancyStagingService>(sp => sp.GetRequiredService<RestOrderOccupancyStagingService>());

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
