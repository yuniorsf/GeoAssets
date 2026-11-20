using GeoAssets.Workflow;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Messaging.ServiceBus;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// This host's workflow integration-event transport (XD01-357, ADR-001 smell M): the transactional outbox every order
/// workflow stages its integration events on, and the broker its relay publishes them to. Domain-neutral — it moved here
/// from the shared-use wiring, which was merely its first user (XD01-316).
/// </summary>
public static class WorkflowMessagingHostExtensions
{
    /// <summary>
    /// Configuration key naming the broker the outbox relay publishes to: <c>Kafka</c> (options from the
    /// <c>WorkflowKafka</c> section), <c>ServiceBus</c> (options from <c>WorkflowServiceBus</c>) or <c>None</c>.
    /// Compared ignoring case; absent or blank means <c>None</c>.
    /// </summary>
    public const string OutboxTransportKey = "Workflow:Outbox:Transport";

    public const string KafkaTransport = "Kafka";

    public const string ServiceBusTransport = "ServiceBus";

    public const string NoTransport = "None";

    /// <summary>
    /// Wires the transactional outbox into this host. The store — and with it <c>IOutboxWriter</c> — is always registered,
    /// on the request's <c>ServiceOrderDbContext</c>, with the Protocol Buffers serializer: an integration event is staged
    /// in the same transaction as the order change it reports, whatever the transport.
    ///
    /// The relay runs only when <see cref="OutboxTransportKey"/> names a broker. With no transport (the default) staged
    /// messages stay in the outbox table as pending: none is lost, none is marked delivered without having been, and they
    /// are published once the host is started with a transport — except the message types a consumer registered through
    /// <see cref="IntegrationConsumerHostExtensions.AddWorkflowIntegrationConsumer{TEvent, THandler}"/> handles in process
    /// (XD01-383: the asset-mutation apply saga's request and replies).
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="OutboxTransportKey"/> names no known transport.</exception>
    public static IServiceCollection AddServerWorkflowMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddWorkflowProtobufMessaging();

        if (!HasBroker(configuration))
        {
            return services.AddWorkflowOutboxStore();
        }

        var transport = configuration[OutboxTransportKey];
        if (string.Equals(transport, KafkaTransport, StringComparison.OrdinalIgnoreCase))
        {
            services.AddKafkaIntegrationPublisher(configuration);
        }
        else if (string.Equals(transport, ServiceBusTransport, StringComparison.OrdinalIgnoreCase))
        {
            services.AddServiceBusIntegrationPublisher(configuration);
        }
        else
        {
            throw new InvalidOperationException(
                $"Configuration '{OutboxTransportKey}' is '{transport}'; expected '{KafkaTransport}', '{ServiceBusTransport}' or '{NoTransport}'.");
        }

        return services.AddWorkflowOutbox();
    }

    /// <summary>Whether <see cref="OutboxTransportKey"/> names a broker (anything but absent, blank or <see cref="NoTransport"/>).</summary>
    public static bool HasBroker(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var transport = configuration[OutboxTransportKey];
        return !string.IsNullOrWhiteSpace(transport)
            && !string.Equals(transport, NoTransport, StringComparison.OrdinalIgnoreCase);
    }
}
