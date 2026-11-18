using GeoAssets.Workflow;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server;

/// <summary>
/// This host's wiring of the shared-use dispatch (XD01-316): the two order types, the dispatch
/// service with its unit of work and telemetry. The transactional outbox its notifications are
/// staged on is the host's workflow messaging (<see cref="WorkflowMessagingHostExtensions"/>, XD01-357).
/// </summary>
public static class SharedUseHostExtensions
{
    /// <summary>
    /// Registers the shared-use order types in <paramref name="registry"/>, with the pole asset
    /// types this host is configured with (<see cref="OrderOccupancyHostExtensions.PoleAssetTypeIds"/>).
    /// </summary>
    public static void RegisterSharedUseOrderTypes(this OrderTypeRegistry registry, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);

        SharedUseOrderTypes.RegisterAll(registry, OrderOccupancyHostExtensions.PoleAssetTypeIds(configuration));
    }

    /// <summary>
    /// Registers the dispatch service behind its two ports, over the stores, the outbox and the
    /// unit of work of this host's <c>ServiceOrderDbContext</c>, with its span and metrics.
    /// Call after <c>AddWorkflowPersistence</c> and
    /// <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/>.
    /// </summary>
    public static IServiceCollection AddServerSharedUseDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddSharedUseDispatch()
            .AddSharedUseDispatchPersistence();
    }
}
