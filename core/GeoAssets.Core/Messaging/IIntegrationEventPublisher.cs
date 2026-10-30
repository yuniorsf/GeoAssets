namespace GeoAssets.Core.Messaging;

/// <summary>
/// Publishes binary <see cref="IntegrationEnvelope"/> messages to an external message broker.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default);

    Task PublishBatchAsync(IReadOnlyList<IntegrationEnvelope> envelopes, CancellationToken ct = default);
}
