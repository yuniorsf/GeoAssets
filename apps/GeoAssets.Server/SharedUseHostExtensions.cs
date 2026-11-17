using GeoAssets.Workflow;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server;

/// <summary>
/// This host's wiring of the shared-use dispatch (XD01-316): the two order types, the dispatch
/// service with its unit of work and telemetry, and the transactional outbox its notifications
/// are staged on.
/// </summary>
public static class SharedUseHostExtensions
{
    /// <summary>
    /// Configuration key naming the broker the outbox relay publishes to: <c>Kafka</c> (options
    /// from the <c>WorkflowKafka</c> section), <c>ServiceBus</c> (options from
    /// <c>WorkflowServiceBus</c>) or <c>None</c>. Compared ignoring case.
    /// </summary>
    public const string OutboxTransportKey = "Workflow:Outbox:Transport";

    public const string KafkaTransport = "Kafka";

    public const string ServiceBusTransport = "ServiceBus";

    public const string NoTransport = "None";

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
    /// Wires the existing transactional outbox into this host. The store — and with it
    /// <c>IOutboxWriter</c> — is always registered, on the request's <c>ServiceOrderDbContext</c>,
    /// with the Protocol Buffers serializer: a notification is staged in the same transaction as
    /// the order it reports, whatever the transport.
    ///
    /// The relay runs only when <see cref="OutboxTransportKey"/> names a broker. With no
    /// transport configured (the default, <see cref="NoTransport"/>) staged messages stay in the
    /// outbox table as pending: none is lost, none is marked delivered without having been, and
    /// they are published once the host is started with a transport — except asset mutation
    /// batches, which <see cref="InProcessAssetMutationHostExtensions.AddServerInProcessAssetMutationApply"/>
    /// applies in process in that case (XD01-337).
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="OutboxTransportKey"/> names no known transport.</exception>
    public static IServiceCollection AddServerWorkflowOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddWorkflowProtobufMessaging();

        var transport = configuration[OutboxTransportKey];
        if (string.IsNullOrWhiteSpace(transport) || string.Equals(transport, NoTransport, StringComparison.OrdinalIgnoreCase))
            return services.AddWorkflowOutboxStore();

        if (string.Equals(transport, KafkaTransport, StringComparison.OrdinalIgnoreCase))
            services.AddKafkaIntegrationPublisher(configuration);
        else if (string.Equals(transport, ServiceBusTransport, StringComparison.OrdinalIgnoreCase))
            services.AddServiceBusIntegrationPublisher(configuration);
        else
        {
            throw new InvalidOperationException(
                $"Configuration '{OutboxTransportKey}' is '{transport}'; expected '{KafkaTransport}', '{ServiceBusTransport}' or '{NoTransport}'.");
        }

        return services.AddWorkflowOutbox();
    }

    /// <summary>
    /// Registers the dispatch service behind its two ports, over the stores, the outbox and the
    /// unit of work of this host's <c>ServiceOrderDbContext</c>, with its span and metrics.
    /// Call after <c>AddWorkflowPersistence</c> and <see cref="AddServerWorkflowOutbox"/>.
    /// </summary>
    public static IServiceCollection AddServerSharedUseDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddSharedUseDispatch()
            .AddSharedUseDispatchPersistence();
    }
}
