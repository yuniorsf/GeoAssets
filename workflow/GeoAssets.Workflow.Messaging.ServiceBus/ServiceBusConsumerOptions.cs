namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// Configuration options for <see cref="ServiceBusIntegrationEventConsumer{TEvent}"/>.
/// </summary>
public sealed class ServiceBusConsumerOptions
{
    public const string SectionName = "WorkflowServiceBusConsumer";

    /// <summary>
    /// The sub-section of <see cref="SectionName"/> holding one named consumer's overrides (XD01-383):
    /// <c>WorkflowServiceBusConsumer:Consumers:{key}</c> — e.g. its own <see cref="SubscriptionName"/> — over the shared settings.
    /// </summary>
    public const string ConsumersSectionName = "Consumers";

    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName { get; set; } = string.Empty;

    public string SubscriptionName { get; set; } = string.Empty;

    public string ConsumerName { get; set; } = "default-servicebus-consumer";

    public string? TargetCatalogInstanceId { get; set; }

    public bool DeadLetterOnCatalogMismatch { get; set; }

    public int MaxConcurrentCalls { get; set; } = 4;

    public TimeSpan InboxRetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// The message types this consumer handles (ordinal, XD01-383); empty — the default — handles every type. A message of
    /// another type is completed without being handled (<see cref="ServiceBusConsumeDisposition.MessageTypeSkipped"/>).
    /// </summary>
    public IList<string> MessageTypes { get; } = [];
}
