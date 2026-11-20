using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Providers;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.NeutralOperator.Application;

/// <summary>
/// The NeutralOperator vertical's workflow registrations (XD01-375, moved out of the generic engine's
/// <c>WorkflowServiceExtensions</c>): staged occupancy, the shared-use dispatch and the occupancy commit.
/// </summary>
public static class NeutralOperatorServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IOrderOccupancyStagingService"/> (XD01-315): staging and discarding
    /// proposed occupancy changes on an order's targets, and reading a target's pole with the
    /// staged preview. Expects <see cref="OrderTypeRegistry"/>, <see cref="ServiceOrderRules"/>,
    /// <see cref="IServiceOrderReader"/>, <see cref="IServiceOrderTargetReader"/>,
    /// <see cref="IPoleOccupancyReader"/> and both change-set ports to be registered by the host.
    /// A target is a pole when its asset type carries <see cref="OrderOccupancyOptions.PoleRole"/> in the host's
    /// <see cref="IAssetProvider"/> (XD01-368), or is one of the deprecated
    /// <see cref="OrderOccupancyOptions.PoleAssetTypeIds"/> <paramref name="configure"/> may still name.
    /// </summary>
    public static IServiceCollection AddOrderOccupancyStaging(
        this IServiceCollection services,
        Action<OrderOccupancyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OrderOccupancyOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddScoped(sp => new OrderOccupancyStagingService(
            sp.GetRequiredService<IServiceOrderReader>(),
            sp.GetRequiredService<OrderTypeRegistry>(),
            sp.GetRequiredService<ServiceOrderRules>(),
            sp.GetRequiredService<IServiceOrderTargetReader>(),
            sp.GetRequiredService<IPoleOccupancyReader>(),
            sp.GetRequiredService<IOccupancyChangeSetReader>(),
            sp.GetRequiredService<IOccupancyChangeSetWriter>(),
            sp.GetRequiredService<OrderOccupancyOptions>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetService<IAssetProvider>()));
        services.TryAddScoped<IOrderOccupancyStagingService>(sp => sp.GetRequiredService<OrderOccupancyStagingService>());

        return services;
    }

    /// <summary>
    /// Registers the shared-use dispatch (XD01-316): one scoped
    /// <see cref="SharedUseDispatchService"/> behind both of its ports,
    /// <see cref="ISharedUseDispatchService"/> and <see cref="ISharedUseDispatchPlanReader"/>.
    ///
    /// The host must have registered <see cref="OrderTypeRegistry"/> (with
    /// <see cref="SharedUseOrderTypes"/> in it), <see cref="ServiceOrderRules"/>,
    /// <see cref="IServiceOrderRepository"/>, both target ports, <see cref="IPoleOccupancyReader"/>,
    /// an <see cref="IOutboxWriter"/> and an <see cref="IWorkflowUnitOfWork"/> over the same stores.
    /// An <see cref="ISharedUseFanOutTracer"/> is used when one is registered.
    /// </summary>
    public static IServiceCollection AddSharedUseDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped(sp => new SharedUseDispatchService(
            sp.GetRequiredService<IServiceOrderRepository>(),
            sp.GetRequiredService<OrderTypeRegistry>(),
            sp.GetRequiredService<ServiceOrderRules>(),
            sp.GetRequiredService<IServiceOrderTargetReader>(),
            sp.GetRequiredService<IServiceOrderTargetWriter>(),
            sp.GetRequiredService<IPoleOccupancyReader>(),
            sp.GetRequiredService<IOutboxWriter>(),
            sp.GetRequiredService<IWorkflowUnitOfWork>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetService<ISharedUseFanOutTracer>()));
        services.TryAddScoped<ISharedUseDispatchService>(sp => sp.GetRequiredService<SharedUseDispatchService>());
        services.TryAddScoped<ISharedUseDispatchPlanReader>(sp => sp.GetRequiredService<SharedUseDispatchService>());

        return services;
    }

    /// <summary>
    /// Registers the approval that commits staged occupancy (XD01-318): one scoped
    /// <see cref="OccupancyCommitService"/> behind <see cref="IOccupancyCommitService"/>. Needs
    /// what <see cref="AddOrderOccupancyStaging"/> needs plus <see cref="IServiceOrderRepository"/>,
    /// <see cref="IPoleOccupancyWriter"/>, <see cref="IOutboxWriter"/>, <see cref="IOccupancyApplySagaStore"/>,
    /// <see cref="IIdempotentInbox"/>, <see cref="ITenantOrganizationScope"/> and
    /// <see cref="IWorkflowUnitOfWork"/>; reads <see cref="OrderOccupancyOptions"/> (the catalog
    /// instance) when registered, the defaults otherwise.
    /// </summary>
    public static IServiceCollection AddOccupancyCommit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped(sp => new OccupancyCommitService(
            sp.GetRequiredService<IServiceOrderRepository>(),
            sp.GetRequiredService<OrderTypeRegistry>(),
            sp.GetRequiredService<ServiceOrderRules>(),
            sp.GetRequiredService<IServiceOrderTargetReader>(),
            sp.GetRequiredService<IPoleOccupancyReader>(),
            sp.GetRequiredService<IPoleOccupancyWriter>(),
            sp.GetRequiredService<IOccupancyChangeSetReader>(),
            sp.GetRequiredService<IOccupancyChangeSetWriter>(),
            sp.GetRequiredService<IOutboxWriter>(),
            sp.GetRequiredService<IOccupancyApplySagaStore>(),
            sp.GetRequiredService<IWorkflowUnitOfWork>(),
            sp.GetService<OrderOccupancyOptions>() ?? new OrderOccupancyOptions(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddScoped<IOccupancyCommitService>(sp => sp.GetRequiredService<OccupancyCommitService>());

        // The approval saga (XD01-383): the kernel's replies and the operator's retry, and the guard that holds an order
        // "approved, awaiting apply" until the catalog answers.
        services.TryAddScoped(sp => new OccupancyApplySagaService(
            sp.GetRequiredService<IServiceOrderRepository>(),
            sp.GetRequiredService<OrderTypeRegistry>(),
            sp.GetRequiredService<ServiceOrderRules>(),
            sp.GetRequiredService<IOccupancyApplySagaStore>(),
            sp.GetRequiredService<IOutboxWriter>(),
            sp.GetRequiredService<IIdempotentInbox>(),
            sp.GetRequiredService<IWorkflowUnitOfWork>(),
            sp.GetRequiredService<ITenantOrganizationScope>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddScoped<IOccupancyApplySagaService>(sp => sp.GetRequiredService<OccupancyApplySagaService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITransitionGuard, OccupancyApplyPendingTransitionGuard>());

        return services;
    }
}
