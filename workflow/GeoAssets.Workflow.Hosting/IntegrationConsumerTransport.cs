namespace GeoAssets.Workflow.Hosting;

/// <summary>How a host delivers one registered integration-event consumer its messages (XD01-383).</summary>
public enum IntegrationConsumerTransport
{
    /// <summary>A broker is configured but names no topic (or, for Service Bus, no subscription) for the consumer: nothing reaches it.</summary>
    None = 0,

    /// <summary>No broker: this host's outbox relay hands the consumer the messages this host stages (<see cref="InProcessIntegrationEventPublisher"/>).</summary>
    InProcess = 1,

    /// <summary>A Kafka consumer in its own group.</summary>
    Kafka = 2,

    /// <summary>A Service Bus consumer on its own subscription.</summary>
    ServiceBus = 3,
}
