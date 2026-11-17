using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Providers;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.SlaGuard;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Workflow;

/// <summary>
/// DI registration helpers for the GeoAssets workflow layer.
/// </summary>
public static class WorkflowServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="OrderTypeRegistry"/> as a singleton pre-populated
    /// with the built-in order types plus any caller additions.
    ///
    /// For DB-backed types call <see cref="LoadRegistryFromDbAsync"/> after the host
    /// is built to merge persisted types on top.
    /// </summary>
    public static IServiceCollection AddOrderTypeRegistry(
        this IServiceCollection services,
        Action<OrderTypeRegistry>? configure = null)
    {
        var registry = new OrderTypeRegistry();
        SeedDefaultOrderTypes(registry);
        configure?.Invoke(registry);
        services.AddSingleton(registry);
        return services;
    }

    /// <summary>
    /// Registers the notification pipeline with a <b>no-op</b> publisher.
    ///
    /// Call one of the messaging-specific extension methods instead to wire a
    /// real transport (Service Bus, Kafka, …).
    /// <code>
    ///   services.AddWorkflowNotifications();            // no-op (default)
    ///   services.AddWorkflowServiceBus(cfg);            // Azure Service Bus
    ///   services.AddWorkflowKafka(cfg);                 // Apache Kafka
    /// </code>
    /// </summary>
    public static IServiceCollection AddWorkflowNotifications(
        this IServiceCollection services)
    {
        services.AddSingleton<IOrderEventPublisher,    NullOrderEventPublisher>();
        services.AddSingleton<IOrderNotificationService, OrderNotificationService>();
        return services;
    }

    /// <summary>
    /// Replaces the default <see cref="NullOrderEventPublisher"/> with a
    /// caller-supplied implementation without changing
    /// <see cref="IOrderNotificationService"/>.
    /// </summary>
    public static IServiceCollection AddWorkflowNotifications<TPublisher>(
        this IServiceCollection services)
        where TPublisher : class, IOrderEventPublisher
    {
        services.AddSingleton<IOrderEventPublisher,    TPublisher>();
        services.AddSingleton<IOrderNotificationService, OrderNotificationService>();
        return services;
    }

    /// <summary>
    /// Registers a single, consistently configured <see cref="ServiceOrderRules"/> singleton,
    /// resolving the <see cref="OrderTypeRegistry"/> registered by
    /// <see cref="AddOrderTypeRegistry"/> if present. Without this, every caller would
    /// otherwise hand-construct its own <see cref="ServiceOrderRules"/> — a risk once both
    /// human-facing and AI-agent-facing callers need to share the exact same role-grant
    /// configuration.
    /// </summary>
    public static IServiceCollection AddServiceOrderRules(
        this IServiceCollection services,
        Action<ServiceOrderRulesOptions>? configure = null)
    {
        var options = new ServiceOrderRulesOptions();
        configure?.Invoke(options);

        services.AddSingleton(sp => new ServiceOrderRules(
            orderTypeRegistry: sp.GetService<OrderTypeRegistry>(),
            roleGrants: options.RoleGrants.Count > 0 ? options.RoleGrants : null,
            unrestrictedRoles: options.UnrestrictedRoles.Count > 0 ? options.UnrestrictedRoles : null,
            recipientRoleGrants: options.RecipientRoleGrants.Count > 0 ? options.RecipientRoleGrants : null,
            allowOrdersWithoutOrganization: options.AllowOrdersWithoutOrganization,
            timeProvider: sp.GetService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Registers <see cref="ISlaBreachQueryService"/> (XD01-233) for whatever
    /// <see cref="IServiceOrderReader"/> is already in DI. Pass a configured
    /// <see cref="SlaGuardEngine"/> (sourced from the current official regulatory text — see
    /// <see cref="SlaMatrixRow"/>'s doc comment) once one exists; without it, every query
    /// correctly reports <see cref="SlaBreachSummary.NotConfigured"/>.
    /// </summary>
    public static IServiceCollection AddSlaBreachReporting(
        this IServiceCollection services,
        SlaGuardEngine? engine = null)
    {
        services.AddScoped<ISlaBreachQueryService>(sp => new SlaBreachQueryService(
            sp.GetRequiredService<IServiceOrderReader>(),
            engine,
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return services;
    }

    /// <summary>
    /// Registers the <see cref="FeatureSelectionRegistry"/> as a singleton holding the built-in
    /// selection strategies plus any <c>GeoAssets.Plugin.*.dll</c> found in
    /// <paramref name="pluginsDirectory"/> (default: a <c>plugins</c> folder beside the host),
    /// and a scoped <see cref="BoundedFeatureSelector"/> over it.
    ///
    /// The selector is scoped because the <see cref="IAssetSpatialQuery"/> it runs on usually is
    /// (a store-backed query bound to the request's tenant). When the host registered none, it
    /// falls back to an <see cref="AssetProviderSpatialQuery"/> over the registered
    /// <see cref="IAssetProvider"/>.
    /// </summary>
    public static IServiceCollection AddFeatureSelection(
        this IServiceCollection services,
        string? pluginsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp => new FeatureSelectionRegistry(
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            pluginsDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"),
            typeof(FeatureSelectionRegistry).Assembly));

        services.TryAddScoped(sp =>
        {
            var assets = sp.GetService<IAssetProvider>();
            var spatialQuery = sp.GetService<IAssetSpatialQuery>()
                ?? new AssetProviderSpatialQuery(assets ?? throw new InvalidOperationException(
                    $"Feature selection needs an {nameof(IAssetSpatialQuery)} or an {nameof(IAssetProvider)} registration."));

            return new BoundedFeatureSelector(
                sp.GetRequiredService<FeatureSelectionRegistry>(),
                spatialQuery,
                assets,
                sp.GetService<TimeProvider>());
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOrderTargetSelectionService"/> (scoped, like the order and target
    /// stores it works on) and its <see cref="OrderTargetSelectionOptions"/> (singleton).
    /// Expects <see cref="AddFeatureSelection"/>, <see cref="AddOrderTypeRegistry"/>,
    /// <see cref="AddServiceOrderRules"/>, an <see cref="IServiceOrderRepository"/> and the two
    /// target-store ports to be registered by the host.
    ///
    /// The concrete <see cref="OrderTargetSelectionService"/> is registered too, so a host can
    /// wrap it (e.g. with an observability decorator) by replacing the interface registration.
    /// </summary>
    public static IServiceCollection AddOrderTargets(
        this IServiceCollection services,
        Action<OrderTargetSelectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OrderTargetSelectionOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddScoped(sp => new OrderTargetSelectionService(
            sp.GetRequiredService<IServiceOrderRepository>(),
            sp.GetRequiredService<OrderTypeRegistry>(),
            sp.GetRequiredService<ServiceOrderRules>(),
            sp.GetRequiredService<BoundedFeatureSelector>(),
            sp.GetRequiredService<IServiceOrderTargetReader>(),
            sp.GetRequiredService<IServiceOrderTargetWriter>(),
            sp.GetRequiredService<OrderTargetSelectionOptions>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddScoped<IOrderTargetSelectionService>(sp => sp.GetRequiredService<OrderTargetSelectionService>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IOrderOccupancyStagingService"/> (XD01-315): staging and discarding
    /// proposed occupancy changes on an order's targets, and reading a target's pole with the
    /// staged preview. Expects <see cref="OrderTypeRegistry"/>, <see cref="ServiceOrderRules"/>,
    /// <see cref="IServiceOrderReader"/>, <see cref="IServiceOrderTargetReader"/>,
    /// <see cref="IPoleOccupancyReader"/> and both change-set ports to be registered by the host.
    /// <paramref name="configure"/> names the host's pole asset types
    /// (<see cref="OrderOccupancyOptions.PoleAssetTypeIds"/>); without any, no target is a pole.
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
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
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

    // ── Default order types ───────────────────────────────────────────────────

    /// <summary>
    /// The workflow graph every built-in order type below uses — identical to the
    /// global default graph in <see cref="ServiceOrderTransitions"/>, expressed as
    /// data on each <see cref="OrderType"/> instead of relying on the implicit
    /// "no States defined" fallback, per XD01-3's per-order-type workflow design.
    /// </summary>
    private static List<WorkflowState> DefaultWorkflowStates() =>
    [
        new(ServiceOrderStatus.Draft,      "Borrador"),
        new(ServiceOrderStatus.Pending,    "Pendiente"),
        new(ServiceOrderStatus.InProgress, "En progreso"),
        new(ServiceOrderStatus.OnHold,     "En espera"),
        new(ServiceOrderStatus.Completed,  "Completado", IsSuccess: true),
        new(ServiceOrderStatus.Cancelled,  "Cancelado"),
    ];

    private static List<WorkflowTransition> DefaultWorkflowTransitions() =>
    [
        new(ServiceOrderStatus.Draft,      ServiceOrderStatus.Pending,    OrderActionType.Dispatch),
        new(ServiceOrderStatus.Draft,      ServiceOrderStatus.Cancelled,  OrderActionType.Cancel),
        new(ServiceOrderStatus.Pending,    ServiceOrderStatus.InProgress, OrderActionType.Execute),
        new(ServiceOrderStatus.Pending,    ServiceOrderStatus.Cancelled,  OrderActionType.Cancel),
        new(ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold),
        new(ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed,  OrderActionType.Complete),
        new(ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled,  OrderActionType.Cancel),
        new(ServiceOrderStatus.OnHold,     ServiceOrderStatus.InProgress),
        new(ServiceOrderStatus.OnHold,     ServiceOrderStatus.Cancelled,  OrderActionType.Cancel),
    ];

    private static void SeedDefaultOrderTypes(OrderTypeRegistry registry)
    {
        registry.Register(new OrderType
        {
            Id          = "inspection",
            DisplayName = "Inspección",
            Description = "Inspección programada de activos en campo.",
            CreationPolicies =
            [
                new(PolicyKind.Role, "FieldTechnician"),
                new(PolicyKind.Role, "Supervisor"),
                new(PolicyKind.Role, "Administrator"),
            ],
            States          = DefaultWorkflowStates(),
            Transitions     = DefaultWorkflowTransitions(),
            InitialStateKey = ServiceOrderStatus.Draft,
        });

        registry.Register(new OrderType
        {
            Id          = "maintenance",
            DisplayName = "Mantenimiento",
            Description = "Trabajo de mantenimiento preventivo o correctivo.",
            CreationPolicies =
            [
                new(PolicyKind.Role, "Supervisor"),
                new(PolicyKind.Role, "Administrator"),
            ],
            States          = DefaultWorkflowStates(),
            Transitions     = DefaultWorkflowTransitions(),
            InitialStateKey = ServiceOrderStatus.Draft,
        });

        registry.Register(new OrderType
        {
            Id          = "emergency-repair",
            DisplayName = "Reparación de emergencia",
            Description = "Intervención urgente para restablecer el servicio.",
            CreationPolicies =
            [
                new(PolicyKind.Role, "Supervisor"),
                new(PolicyKind.Role, "Administrator"),
                new(PolicyKind.Permission, "serviceorders:create"),
            ],
            States          = DefaultWorkflowStates(),
            Transitions     = DefaultWorkflowTransitions(),
            InitialStateKey = ServiceOrderStatus.Draft,
        });
    }
}
