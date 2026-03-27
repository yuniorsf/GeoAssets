namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// Configuration for <see cref="ServiceBusOrderEventPublisher"/>.
/// Bind from <c>appsettings.json</c>:
/// <code>
/// "WorkflowServiceBus": {
///   "ConnectionString": "Endpoint=sb://...",
///   "TopicName": "order-state-changed"
/// }
/// </code>
/// </summary>
public sealed class ServiceBusPublisherOptions
{
    public const string SectionName = "WorkflowServiceBus";

    /// <summary>Azure Service Bus connection string or fully-qualified namespace (for managed identity).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Topic (or queue) name to send events to.</summary>
    public string TopicName { get; set; } = "order-state-changed";

    /// <summary>
    /// Maximum number of messages to batch before sending.
    /// Defaults to 1 (send immediately on each event).
    /// </summary>
    public int MaxBatchSize { get; set; } = 1;

    /// <summary>
    /// Optional subject / label set on every <see cref="Azure.Messaging.ServiceBus.ServiceBusMessage"/>.
    /// </summary>
    public string MessageSubject { get; set; } = "OrderStateChanged";
}
