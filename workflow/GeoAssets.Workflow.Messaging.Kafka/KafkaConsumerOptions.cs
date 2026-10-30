namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// Configuration options for <see cref="KafkaIntegrationEventConsumer{TEvent}"/>.
/// </summary>
public sealed class KafkaConsumerOptions
{
    public const string SectionName = "WorkflowKafkaConsumer";

    public string BootstrapServers { get; set; } = "localhost:9092";

    public string TopicName { get; set; } = string.Empty;

    public string GroupId { get; set; } = "geoassets-consumer";

    public string ConsumerName { get; set; } = "default-kafka-consumer";

    public string? TargetCatalogInstanceId { get; set; }

    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan InboxRetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    public Dictionary<string, string> AdditionalConfig { get; init; } = new(StringComparer.Ordinal);
}
