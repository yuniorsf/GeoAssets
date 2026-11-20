namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// Configuration options for <see cref="KafkaIntegrationEventConsumer{TEvent}"/>.
/// </summary>
public sealed class KafkaConsumerOptions
{
    public const string SectionName = "WorkflowKafkaConsumer";

    /// <summary>
    /// The sub-section of <see cref="SectionName"/> holding one named consumer's overrides (XD01-383):
    /// <c>WorkflowKafkaConsumer:Consumers:{key}</c> — e.g. its own <see cref="GroupId"/> — over the shared settings.
    /// </summary>
    public const string ConsumersSectionName = "Consumers";

    public string BootstrapServers { get; set; } = "localhost:9092";

    public string TopicName { get; set; } = string.Empty;

    public string GroupId { get; set; } = "geoassets-consumer";

    public string ConsumerName { get; set; } = "default-kafka-consumer";

    public string? TargetCatalogInstanceId { get; set; }

    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan InboxRetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    public Dictionary<string, string> AdditionalConfig { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The message types this consumer handles (ordinal, XD01-383); empty — the default — handles every type. A message of
    /// another type on the topic is skipped (its offset committed, nothing handled), so several consumers can share one
    /// topic, each in its own group.
    /// </summary>
    public IList<string> MessageTypes { get; } = [];
}
