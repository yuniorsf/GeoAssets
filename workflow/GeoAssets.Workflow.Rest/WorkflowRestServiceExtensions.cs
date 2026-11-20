using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest.Routing;
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
    /// The clients translate a vertical's problem codes with every registered <see cref="IWorkflowProblemMapper"/>, resolved
    /// when each client is first built; no vertical's ports are registered here (XD01-388) — a vertical registers its own.
    /// </summary>
    public static IServiceCollection AddWorkflowRest(this IServiceCollection services, string baseUrl)
    {
        services.AddHttpClient();

        services.AddSingleton(sp => new RestServiceOrderRepository(BuildClient(sp, baseUrl), sp.GetServices<IWorkflowProblemMapper>()));
        services.AddSingleton<IServiceOrderRepository>(sp => sp.GetRequiredService<RestServiceOrderRepository>());

        // Read-only or write-only consumers can depend on just the piece they need
        // instead of the full IServiceOrderRepository.
        services.AddSingleton<IServiceOrderReader>(sp => sp.GetRequiredService<IServiceOrderRepository>());
        services.AddSingleton<IServiceOrderWriter>(sp => sp.GetRequiredService<IServiceOrderRepository>());

        services.AddSingleton(sp => new RestOrderTypeRepository(BuildClient(sp, baseUrl)));
        services.AddSingleton<IOrderTypeRepository>(sp => sp.GetRequiredService<RestOrderTypeRepository>());

        // Service-order targets and selection (XD01-308) — same named client, same lifetime.
        services.AddSingleton(sp => new RestServiceOrderTargetRepository(BuildClient(sp, baseUrl), sp.GetServices<IWorkflowProblemMapper>()));
        services.AddSingleton<IServiceOrderTargetReader>(sp => sp.GetRequiredService<RestServiceOrderTargetRepository>());
        services.AddSingleton<IServiceOrderTargetWriter>(sp => sp.GetRequiredService<RestServiceOrderTargetRepository>());

        services.AddSingleton(sp => new RestOrderTargetSelectionService(BuildClient(sp, baseUrl), sp.GetServices<IWorkflowProblemMapper>()));
        services.AddSingleton<IOrderTargetSelectionService>(sp => sp.GetRequiredService<RestOrderTargetSelectionService>());

        services.AddSingleton(sp => new RestMapSelectionClient(BuildClient(sp, baseUrl), sp.GetServices<IWorkflowProblemMapper>()));

        return services;
    }

    /// <summary>
    /// Registers the same ports as <see cref="AddWorkflowRest"/>, routed by order-type descriptor (XD01-384, ADR-001 Phase 7)
    /// for a client that reaches every workflow API through one gateway: <paramref name="baseUrl"/> is the kernel's workflow
    /// API root on the gateway (e.g. <c>https://gateway/api/workflow</c>), and each order type whose descriptor in the kernel's
    /// registry names an <see cref="Registry.OrderTypeDescriptor.ApiBaseRoute"/> is reached at that route on the same origin
    /// — see <see cref="DescriptorOrderApiRoutes"/> (registry read every <paramref name="refreshInterval"/>, default
    /// <see cref="DescriptorOrderApiRoutes.DefaultRefreshInterval"/>), <see cref="ServiceOrderRouteLocator"/> (orders known by
    /// id) and <see cref="DescriptorRoutedServiceOrderRepository"/> (lists merged across the roots). Map gestures
    /// (<see cref="RestMapSelectionClient"/>) stay on the kernel's root: selection is the kernel's. As in
    /// <see cref="AddWorkflowRest"/>, the clients use every registered <see cref="IWorkflowProblemMapper"/> and no vertical's
    /// ports are registered here: a vertical routes its own over this routing (<see cref="IOrderApiRoutes"/>,
    /// <see cref="ServiceOrderRouteLocator"/>, <see cref="ClientsByRoot{TClient}"/>).
    /// </summary>
    public static IServiceCollection AddDescriptorRoutedWorkflowRest(
        this IServiceCollection services, string baseUrl, TimeSpan? refreshInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        services.AddHttpClient();
        var defaultRoot = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        services.AddSingleton<IOrderApiRoutes>(sp => new DescriptorOrderApiRoutes(
            BuildClient(sp, defaultRoot),
            defaultRoot,
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            refreshInterval ?? DescriptorOrderApiRoutes.DefaultRefreshInterval));

        services.AddSingleton(sp => new ClientsByRoot<RestServiceOrderRepository>(root => new RestServiceOrderRepository(BuildClient(sp, root), sp.GetServices<IWorkflowProblemMapper>())));
        services.AddSingleton(sp => new ClientsByRoot<RestOrderTypeRepository>(root => new RestOrderTypeRepository(BuildClient(sp, root))));
        services.AddSingleton(sp => new ClientsByRoot<RestServiceOrderTargetRepository>(root => new RestServiceOrderTargetRepository(BuildClient(sp, root), sp.GetServices<IWorkflowProblemMapper>())));
        services.AddSingleton(sp => new ClientsByRoot<RestOrderTargetSelectionService>(root => new RestOrderTargetSelectionService(BuildClient(sp, root), sp.GetServices<IWorkflowProblemMapper>())));
        services.AddSingleton<ServiceOrderRouteLocator>();

        services.AddSingleton<DescriptorRoutedServiceOrderRepository>();
        services.AddSingleton<IServiceOrderRepository>(sp => sp.GetRequiredService<DescriptorRoutedServiceOrderRepository>());
        services.AddSingleton<IServiceOrderReader>(sp => sp.GetRequiredService<IServiceOrderRepository>());
        services.AddSingleton<IServiceOrderWriter>(sp => sp.GetRequiredService<IServiceOrderRepository>());

        services.AddSingleton<DescriptorRoutedOrderTypeRepository>();
        services.AddSingleton<IOrderTypeRepository>(sp => sp.GetRequiredService<DescriptorRoutedOrderTypeRepository>());

        services.AddSingleton<DescriptorRoutedServiceOrderTargets>();
        services.AddSingleton<IServiceOrderTargetReader>(sp => sp.GetRequiredService<DescriptorRoutedServiceOrderTargets>());
        services.AddSingleton<IServiceOrderTargetWriter>(sp => sp.GetRequiredService<DescriptorRoutedServiceOrderTargets>());

        services.AddSingleton<DescriptorRoutedOrderTargetSelectionService>();
        services.AddSingleton<IOrderTargetSelectionService>(sp => sp.GetRequiredService<DescriptorRoutedOrderTargetSelectionService>());

        services.AddSingleton(sp => new RestMapSelectionClient(BuildClient(sp, defaultRoot), sp.GetServices<IWorkflowProblemMapper>()));

        return services;
    }

    private static HttpClient BuildClient(IServiceProvider sp, string baseUrl) =>
        BuildClient(sp, new Uri(baseUrl.TrimEnd('/') + "/"));

    private static HttpClient BuildClient(IServiceProvider sp, Uri baseAddress) => CreateWorkflowHttpClient(sp, baseAddress);

    /// <summary>
    /// The host's token-carrying workflow client (the named <c>GeoAssetsServer</c> client of <see cref="IHttpClientFactory"/>)
    /// based at <paramref name="baseAddress"/> — a workflow API root with a trailing slash. A vertical's REST registration
    /// (XD01-388) builds its clients with it, so they carry the same token as the chassis'.
    /// </summary>
    public static HttpClient CreateWorkflowHttpClient(IServiceProvider sp, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // Named (not the anonymous default) so a host's auth handler on this name also covers
        // ServiceOrders/OrderType calls — same reasoning as RestProviderFactory.BuildClient and
        // GeoIdentityRestExtensions.CreateIdentityClient. GeoAssets.Web attaches its CIAM
        // AuthorizationMessageHandler only to "GeoAssetsServer" (Program.cs, XD01-17); the
        // anonymous client this used to request never carried a token, so every call 401'd
        // against GeoAssets.Server's FallbackPolicy.RequireAuthenticatedUser() (XD01-127).
        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
        client.BaseAddress = baseAddress;
        return client;
    }
}
