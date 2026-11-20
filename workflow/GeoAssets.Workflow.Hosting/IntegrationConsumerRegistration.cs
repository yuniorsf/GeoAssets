namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// One integration-event consumer a host registers through
/// <see cref="IntegrationConsumerHostExtensions.AddWorkflowIntegrationConsumer{TEvent, THandler}"/> (XD01-383).
/// </summary>
/// <param name="ConsumerKey">
/// Its configuration key: a broker consumer reads its overrides from <c>WorkflowKafkaConsumer:Consumers:{key}</c> or
/// <c>WorkflowServiceBusConsumer:Consumers:{key}</c>.
/// </param>
/// <param name="ConsumerName">The inbox consumer name its deliveries are deduplicated under, whatever the transport.</param>
/// <param name="MessageType">The one message type it handles; every other type on the topic is skipped.</param>
public sealed record IntegrationConsumerRegistration(string ConsumerKey, string ConsumerName, string MessageType)
{
    /// <summary>
    /// The catalog instance a broker consumer fences on (messages addressed to another one are skipped), unless its
    /// configuration names one; null fences nothing.
    /// </summary>
    public string? TargetCatalogInstanceId { get; init; }
}
