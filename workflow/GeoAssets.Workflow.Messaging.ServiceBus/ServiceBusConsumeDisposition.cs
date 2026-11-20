namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// Deterministic settlement outcome produced by <see cref="ServiceBusIntegrationEventConsumer{TEvent}"/>.
/// </summary>
public enum ServiceBusConsumeDisposition
{
    Complete = 0,
    DuplicateSkipped = 1,
    AbandonTransient = 2,
    DeadLetterPoison = 3,
    CatalogShardFenced = 4,

    /// <summary>The message's type is not one the consumer handles (XD01-383); completed without being handled.</summary>
    MessageTypeSkipped = 5,
}
