namespace GeoAssets.Core.Messaging;

/// <summary>
/// Stages outbound integration events into the transactional outbox store.
/// </summary>
public interface IOutboxWriter
{
    Task<Guid> EnqueueAsync<TEvent>(
        TEvent message,
        string messageType,
        string partitionKey,
        string? catalogInstanceId = null,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken ct = default)
        where TEvent : notnull;

    Task EnqueueEnvelopeAsync(IntegrationEnvelope envelope, CancellationToken ct = default);
}
