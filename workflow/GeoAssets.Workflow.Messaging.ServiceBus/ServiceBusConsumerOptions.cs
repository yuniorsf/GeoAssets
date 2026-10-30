namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// Configuration options for <see cref="ServiceBusIntegrationEventConsumer{TEvent}"/>.
/// </summary>
public sealed class ServiceBusConsumerOptions
{
    public const string SectionName = "WorkflowServiceBusConsumer";

    public string ConnectionString { get; set; } = string.Empty;

    public string TopicName { get; set; } = string.Empty;

    public string SubscriptionName { get; set; } = string.Empty;

    public string ConsumerName { get; set; } = "default-servicebus-consumer";

    public string? TargetCatalogInstanceId { get; set; }

    public bool DeadLetterOnCatalogMismatch { get; set; }

    public int MaxConcurrentCalls { get; set; } = 4;

    public TimeSpan InboxRetentionPeriod { get; set; } = TimeSpan.FromDays(7);
}
