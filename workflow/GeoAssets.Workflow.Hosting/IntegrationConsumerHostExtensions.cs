using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Registers an integration-event consumer on whatever transport this host has (XD01-383), so the same handler runs —
/// with the same inbox deduplication — whether its messages come from a broker or from this host's own outbox:
/// <list type="bullet">
/// <item><description><b>A broker</b> (<see cref="WorkflowMessagingHostExtensions.OutboxTransportKey"/>) whose consumer
/// section names a topic (Kafka: <c>WorkflowKafkaConsumer:TopicName</c>, shared or under <c>Consumers:{key}</c>; Service Bus:
/// <c>TopicName</c> and the consumer's own <c>Consumers:{key}:SubscriptionName</c>): a broker consumer of its own — Kafka
/// group <c>{GroupId}.{key}</c> unless <c>Consumers:{key}:GroupId</c> says otherwise — filtered to the one message type.</description></item>
/// <item><description><b>A broker with no topic for it</b>: nothing (a host configured for publishing only boots as before;
/// the messages wait on the broker).</description></item>
/// <item><description><b>No broker</b>: an <see cref="InProcessIntegrationRoute"/>, and this host's outbox relay claiming
/// the routed message types only, with <see cref="InProcessIntegrationEventPublisher"/> as its "broker" — every other
/// staged message stays pending, as before. In process a host reaches only itself: the messages it stages are the ones
/// its consumers get.</description></item>
/// </list>
/// Call after <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/> (the outbox, inbox and serializer).
/// </summary>
public static class IntegrationConsumerHostExtensions
{
    /// <summary>The worker id the in-process relay claims outbox messages under.</summary>
    public const string InProcessRelayWorkerId = "in-process-integration";

    /// <summary>Which transport <see cref="AddWorkflowIntegrationConsumer{TEvent, THandler}"/> uses for <paramref name="registration"/>.</summary>
    public static IntegrationConsumerTransport TransportFor(IConfiguration configuration, IntegrationConsumerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(registration);

        if (!WorkflowMessagingHostExtensions.HasBroker(configuration)) return IntegrationConsumerTransport.InProcess;

        var transport = configuration[WorkflowMessagingHostExtensions.OutboxTransportKey];
        if (string.Equals(transport, WorkflowMessagingHostExtensions.KafkaTransport, StringComparison.OrdinalIgnoreCase))
        {
            var options = WorkflowKafkaServiceExtensions.BindNamedConsumerOptions(configuration, registration.ConsumerKey);
            return string.IsNullOrWhiteSpace(options.TopicName) ? IntegrationConsumerTransport.None : IntegrationConsumerTransport.Kafka;
        }

        if (string.Equals(transport, WorkflowMessagingHostExtensions.ServiceBusTransport, StringComparison.OrdinalIgnoreCase))
        {
            var options = WorkflowServiceBusServiceExtensions.BindNamedConsumerOptions(configuration, registration.ConsumerKey);
            if (string.IsNullOrWhiteSpace(options.TopicName)) return IntegrationConsumerTransport.None;

            // A topic with no subscription of this consumer's own would read nothing: refuse to start rather than leave the
            // consumer silently absent (XD01-383 — an approval saga would then wait until its timeout).
            if (string.IsNullOrWhiteSpace(NamedValue(
                    configuration, ServiceBusConsumerOptions.SectionName, ServiceBusConsumerOptions.ConsumersSectionName, registration.ConsumerKey, nameof(ServiceBusConsumerOptions.SubscriptionName))))
            {
                throw new InvalidOperationException(
                    $"The Service Bus consumer '{registration.ConsumerKey}' reads topic '{options.TopicName}' but has no subscription: set " +
                    $"{ServiceBusConsumerOptions.SectionName}:{ServiceBusConsumerOptions.ConsumersSectionName}:{registration.ConsumerKey}:{nameof(ServiceBusConsumerOptions.SubscriptionName)}.");
            }

            return IntegrationConsumerTransport.ServiceBus;
        }

        return IntegrationConsumerTransport.None;
    }

    /// <summary>Registers <typeparamref name="THandler"/> for <paramref name="registration"/> on this host's transport (see the type's summary).</summary>
    public static IServiceCollection AddWorkflowIntegrationConsumer<TEvent, THandler>(
        this IServiceCollection services,
        IConfiguration configuration,
        IntegrationConsumerRegistration registration)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(registration);

        switch (TransportFor(configuration, registration))
        {
            case IntegrationConsumerTransport.InProcess:
                services.TryAddScoped<IIntegrationEventHandler<TEvent>, THandler>();
                services.AddSingleton<InProcessIntegrationRoute>(
                    new InProcessIntegrationRoute<TEvent>(registration.MessageType, registration.ConsumerName));
                AddInProcessRelay(services, registration.MessageType);
                break;

            case IntegrationConsumerTransport.Kafka:
                var groupOverridden = !string.IsNullOrWhiteSpace(NamedValue(
                    configuration, KafkaConsumerOptions.SectionName, KafkaConsumerOptions.ConsumersSectionName, registration.ConsumerKey, nameof(KafkaConsumerOptions.GroupId)));
                services.AddKafkaIntegrationConsumer<TEvent, THandler>(configuration, registration.ConsumerKey, options =>
                {
                    options.ConsumerName = registration.ConsumerName;
                    if (!groupOverridden) options.GroupId = $"{options.GroupId}.{registration.ConsumerKey}";
                    options.TargetCatalogInstanceId ??= registration.TargetCatalogInstanceId;
                    options.MessageTypes.Clear();
                    options.MessageTypes.Add(registration.MessageType);
                });
                break;

            case IntegrationConsumerTransport.ServiceBus:
                services.AddServiceBusIntegrationConsumer<TEvent, THandler>(configuration, registration.ConsumerKey, options =>
                {
                    options.ConsumerName = registration.ConsumerName;
                    options.TargetCatalogInstanceId ??= registration.TargetCatalogInstanceId;
                    options.MessageTypes.Clear();
                    options.MessageTypes.Add(registration.MessageType);
                });
                break;

            case IntegrationConsumerTransport.None:
            default:
                break;
        }

        return services;
    }

    /// <summary>
    /// The relay of a host with no broker, restricted to the routed message types (never empty: an empty list would claim
    /// every type), publishing through <see cref="InProcessIntegrationEventPublisher"/>.
    /// </summary>
    private static void AddInProcessRelay(IServiceCollection services, string messageType)
    {
        services.TryAddSingleton<IIntegrationEventPublisher>(sp => new InProcessIntegrationEventPublisher(
            sp.GetServices<InProcessIntegrationRoute>(), sp));
        services.AddOptions<OutboxRelayOptions>().Configure(options =>
        {
            options.WorkerId = InProcessRelayWorkerId;
            if (!options.MessageTypes.Contains(messageType, StringComparer.Ordinal))
                options.MessageTypes.Add(messageType);
        });
        services.AddHostedService<OutboxRelayProcessor>();
    }

    private static string? NamedValue(IConfiguration configuration, string section, string consumers, string key, string setting) =>
        configuration[$"{section}:{consumers}:{key}:{setting}"];
}
