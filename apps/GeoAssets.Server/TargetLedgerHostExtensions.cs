using GeoAssets.Core.Services;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server;

/// <summary>
/// The kernel's global target ledger (XD01-379, ADR-001 §3.4): its store, its service and — when this host has a broker —
/// the consumer of the verticals' <c>OrderStateChangedV1</c> that releases a remote order's targets when it closes.
/// Domain-neutral and module-free: the ledger works with <c>GeoAssets:Modules = []</c>.
/// </summary>
public static class TargetLedgerHostExtensions
{
    /// <summary>
    /// Registers the ledger. Call after <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/> (the
    /// outbox and inbox it writes through), the order-target services and the order-type descriptor registry.
    ///
    /// The consumer runs only when <see cref="WorkflowMessagingHostExtensions.OutboxTransportKey"/> names a broker and that
    /// broker's consumer section names the topic to read (<c>WorkflowKafkaConsumer:TopicName</c>, or
    /// <c>WorkflowServiceBusConsumer:TopicName</c> and <c>:SubscriptionName</c>) — a host configured for publishing only
    /// keeps booting as before. Without it, remote orders' targets are released only through the release route.
    /// </summary>
    public static IServiceCollection AddServerTargetLedger(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddTargetLedgerPersistence();
        services.TryAddScoped<ITenantOrganizationScope, TenantOrganizationScope>();
        services.TryAddScoped(sp => new TargetLedgerService(
            sp.GetRequiredService<OrderTargetSelectionService>(),
            sp.GetRequiredService<ITargetLedgerStore>(),
            sp.GetRequiredService<IWorkflowUnitOfWork>(),
            sp.GetRequiredService<IOutboxWriter>(),
            sp.GetRequiredService<IIdempotentInbox>(),
            sp.GetRequiredService<IOrderTypeDescriptorRegistry>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetRequiredService<ITenantOrganizationScope>()));

        if (!WorkflowMessagingHostExtensions.HasBroker(configuration)) return services;

        var transport = configuration[WorkflowMessagingHostExtensions.OutboxTransportKey];
        if (string.Equals(transport, WorkflowMessagingHostExtensions.KafkaTransport, StringComparison.OrdinalIgnoreCase)
            && Configured(configuration, KafkaConsumerOptions.SectionName, "TopicName"))
        {
            services.AddKafkaIntegrationConsumer<OrderStateChangedEvent, TargetLedgerOrderStateChangedHandler>(configuration);
        }
        else if (string.Equals(transport, WorkflowMessagingHostExtensions.ServiceBusTransport, StringComparison.OrdinalIgnoreCase)
            && Configured(configuration, ServiceBusConsumerOptions.SectionName, "TopicName", "SubscriptionName"))
        {
            services.AddServiceBusIntegrationConsumer<OrderStateChangedEvent, TargetLedgerOrderStateChangedHandler>(configuration);
        }

        return services;
    }

    private static bool Configured(IConfiguration configuration, string section, params string[] keys) =>
        keys.All(key => !string.IsNullOrWhiteSpace(configuration[$"{section}:{key}"]));
}
