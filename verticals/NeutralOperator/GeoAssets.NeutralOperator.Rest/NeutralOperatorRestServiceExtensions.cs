using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Rest.Routing;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rest.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.NeutralOperator.Rest;

/// <summary>
/// DI registration of the NeutralOperator vertical's REST clients (XD01-388) — the ports the chassis' REST registration
/// (<see cref="WorkflowRestServiceExtensions"/>) no longer registers: <see cref="IOrderOccupancyStagingService"/>
/// (XD01-315), <see cref="ISharedUseDispatchService"/> and <see cref="ISharedUseDispatchPlanReader"/> (XD01-316) — and of
/// <see cref="NeutralOperatorProblemMapper"/>, through which the chassis' clients translate this vertical's problem codes.
/// </summary>
public static class NeutralOperatorRestServiceExtensions
{
    /// <summary>
    /// The counterpart of <see cref="WorkflowRestServiceExtensions.AddWorkflowRest"/>: registers
    /// <see cref="RestOrderOccupancyStagingService"/> and <see cref="RestSharedUseDispatchClient"/> (one client behind both
    /// dispatch ports) as singletons pointed at <paramref name="baseUrl"/> — the workflow API root — over the host's
    /// token-carrying client, and <see cref="NeutralOperatorProblemMapper"/> as an <see cref="IWorkflowProblemMapper"/>.
    /// Call it before or after <c>AddWorkflowRest</c>: the order does not matter.
    /// </summary>
    public static IServiceCollection AddNeutralOperatorWorkflowRest(this IServiceCollection services, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        services.AddHttpClient();
        services.AddNeutralOperatorProblemMapper();
        var root = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        services.AddSingleton(sp => new RestOrderOccupancyStagingService(WorkflowRestServiceExtensions.CreateWorkflowHttpClient(sp, root)));
        services.AddSingleton<IOrderOccupancyStagingService>(sp => sp.GetRequiredService<RestOrderOccupancyStagingService>());

        services.AddSingleton(sp => new RestSharedUseDispatchClient(WorkflowRestServiceExtensions.CreateWorkflowHttpClient(sp, root)));
        services.AddSingleton<ISharedUseDispatchService>(sp => sp.GetRequiredService<RestSharedUseDispatchClient>());
        services.AddSingleton<ISharedUseDispatchPlanReader>(sp => sp.GetRequiredService<RestSharedUseDispatchClient>());

        return services;
    }

    /// <summary>
    /// The counterpart of <see cref="WorkflowRestServiceExtensions.AddDescriptorRoutedWorkflowRest"/> (XD01-384): registers
    /// <see cref="DescriptorRoutedOrderOccupancyStagingService"/> and <see cref="DescriptorRoutedSharedUseDispatchClient"/>,
    /// which send each request to the host that serves the order, and <see cref="NeutralOperatorProblemMapper"/> as an
    /// <see cref="IWorkflowProblemMapper"/>. It reuses the chassis' routing — <see cref="IOrderApiRoutes"/>,
    /// <see cref="ServiceOrderRouteLocator"/> and the token-carrying client — so call <c>AddDescriptorRoutedWorkflowRest</c>
    /// too (before or after: the services resolve it when first used).
    /// </summary>
    public static IServiceCollection AddDescriptorRoutedNeutralOperatorRest(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();
        services.AddNeutralOperatorProblemMapper();

        services.AddSingleton(sp => new ClientsByRoot<RestOrderOccupancyStagingService>(
            root => new RestOrderOccupancyStagingService(WorkflowRestServiceExtensions.CreateWorkflowHttpClient(sp, root))));
        services.AddSingleton(sp => new ClientsByRoot<RestSharedUseDispatchClient>(
            root => new RestSharedUseDispatchClient(WorkflowRestServiceExtensions.CreateWorkflowHttpClient(sp, root))));

        services.AddSingleton<DescriptorRoutedOrderOccupancyStagingService>();
        services.AddSingleton<IOrderOccupancyStagingService>(sp => sp.GetRequiredService<DescriptorRoutedOrderOccupancyStagingService>());

        services.AddSingleton<DescriptorRoutedSharedUseDispatchClient>();
        services.AddSingleton<ISharedUseDispatchService>(sp => sp.GetRequiredService<DescriptorRoutedSharedUseDispatchClient>());
        services.AddSingleton<ISharedUseDispatchPlanReader>(sp => sp.GetRequiredService<DescriptorRoutedSharedUseDispatchClient>());

        return services;
    }

    /// <summary>Registers <see cref="NeutralOperatorProblemMapper"/> once, however many of the registrations above run.</summary>
    private static void AddNeutralOperatorProblemMapper(this IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowProblemMapper, NeutralOperatorProblemMapper>());
}
