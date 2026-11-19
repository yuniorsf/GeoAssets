using GeoAssets.NeutralOperator.Application;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Workflow;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// This host's wiring of the shared-use dispatch (XD01-316): the two order types, the dispatch
/// service with its unit of work and telemetry. The transactional outbox its notifications are
/// staged on is the host's workflow messaging (<see cref="WorkflowMessagingHostExtensions"/>, XD01-357).
/// </summary>
public static class SharedUseHostExtensions
{
    /// <summary>
    /// Registers the shared-use order types in <paramref name="registry"/>: they target every asset type carrying the
    /// <c>occupiable-support</c> role (XD01-368), so no host configuration names the poles.
    /// </summary>
    public static void RegisterSharedUseOrderTypes(this OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        SharedUseOrderTypes.RegisterAll(registry);
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

        // The shared-use routes' problem answers (XD01-363).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProblemDetailsMapper, SharedUseProblemMapper>());

        return services
            .AddSharedUseDispatch()
            .AddSharedUseDispatchPersistence();
    }
}
