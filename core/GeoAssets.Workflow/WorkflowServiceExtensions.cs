using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.DependencyInjection;

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
            recipientRoleGrants: options.RecipientRoleGrants.Count > 0 ? options.RecipientRoleGrants : null));

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
