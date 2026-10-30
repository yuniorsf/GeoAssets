namespace GeoAssets.Core.Messaging;

/// <summary>
/// Processes a deserialized integration event delivered by a message broker consumer.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : notnull
{
    Task HandleAsync(TEvent message, IntegrationMessageContext context, CancellationToken ct = default);
}
