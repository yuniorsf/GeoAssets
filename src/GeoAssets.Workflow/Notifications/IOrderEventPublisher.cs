namespace GeoAssets.Workflow.Notifications;

/// <summary>
/// Low-level transport abstraction.  A publisher is responsible only for
/// delivering the event to the underlying messaging infrastructure
/// (Service Bus topic, Kafka topic, in-process bus, …).
///
/// Implementations live in separate infrastructure projects
/// (e.g. <c>GeoAssets.Workflow.Messaging.ServiceBus</c>) so the workflow
/// domain has zero dependency on any messaging SDK.
/// </summary>
public interface IOrderEventPublisher
{
    /// <summary>
    /// Publishes a single <see cref="OrderStateChangedEvent"/> to the
    /// underlying transport.
    /// </summary>
    /// <param name="evt">The event to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(OrderStateChangedEvent evt, CancellationToken ct = default);
}
