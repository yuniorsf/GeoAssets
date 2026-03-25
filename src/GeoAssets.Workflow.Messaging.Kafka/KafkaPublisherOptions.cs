namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// Configuration for <see cref="KafkaOrderEventPublisher"/>.
/// Bind from <c>appsettings.json</c>:
/// <code>
/// "WorkflowKafka": {
///   "BootstrapServers": "localhost:9092",
///   "TopicName": "order-state-changed",
///   "ClientId": "geoassets-workflow"
/// }
/// </code>
/// </summary>
public sealed class KafkaPublisherOptions
{
    public const string SectionName = "WorkflowKafka";

    /// <summary>Comma-separated list of Kafka broker addresses.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Topic to which <see cref="GeoAssets.Workflow.Notifications.OrderStateChangedEvent"/> messages are produced.</summary>
    public string TopicName { get; set; } = "order-state-changed";

    /// <summary>
    /// Kafka <c>client.id</c> — used for logging and metrics on the broker side.
    /// </summary>
    public string ClientId { get; set; } = "geoassets-workflow";

    /// <summary>
    /// Additional raw Confluent.Kafka producer config key/value pairs.
    /// Applied after the built-in defaults, allowing fine-grained tuning
    /// (e.g. <c>acks</c>, <c>compression.type</c>, <c>linger.ms</c>).
    /// </summary>
    public Dictionary<string, string> AdditionalConfig { get; set; } = new();

    /// <summary>
    /// When true, the <see cref="Orders.ServiceOrder.Id"/> is used as the Kafka
    /// message key, ensuring all events for the same order land on the same
    /// partition (preserving order).  Defaults to true.
    /// </summary>
    public bool UseOrderIdAsKey { get; set; } = true;
}
