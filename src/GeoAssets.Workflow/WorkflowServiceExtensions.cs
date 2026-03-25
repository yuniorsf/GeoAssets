using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Workflow;

/// <summary>
/// DI registration helpers for the GeoAssets workflow layer.
/// </summary>
public static class WorkflowServiceExtensions
{
    /// <summary>
    /// Registers only the in-memory implementations (no database required).
    /// Useful for unit tests and WASM hosts.
    /// </summary>
    public static IServiceCollection AddWorkflowInMemory(this IServiceCollection services)
    {
        services.AddSingleton<IServiceOrderRepository, InMemoryServiceOrderRepository>();
        services.AddSingleton<IOrderTypeRepository,    InMemoryOrderTypeRepository>();
        return services;
    }

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

    // ── Default order types ───────────────────────────────────────────────────

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
        });
    }
}
