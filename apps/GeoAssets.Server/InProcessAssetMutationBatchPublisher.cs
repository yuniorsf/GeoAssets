using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Server;

/// <summary>
/// The outbox relay's "broker" when this host has none (XD01-337): publishing an
/// <see cref="IntegrationEventTypes.AssetMutationBatchRequestedV1"/> envelope applies its batch
/// to this host's own asset catalog through <see cref="IAssetMutationBatchApplier"/>, in process.
/// The relay's lease, exponential backoff and attempt limit apply unchanged: a batch that fails is
/// marked failed and retried later, and after the relay's last attempt stays in the outbox as
/// failed — never marked delivered without having been applied.
///
/// Delivers nothing else: any other message type, or a batch addressed to a catalog instance
/// other than the one this host is, is refused with an exception (the relay this
/// host runs claims asset batches only, so neither should reach it).
/// </summary>
public sealed class InProcessAssetMutationBatchPublisher : IIntegrationEventPublisher
{
    private readonly IIntegrationMessageSerializer _serializer;
    private readonly IAssetMutationBatchApplier _applier;
    private readonly string _catalogInstanceId;

    /// <param name="serializer">Reads the batch out of the envelope's payload.</param>
    /// <param name="applier">Applies the batch in the batch's organization, once per batch id.</param>
    /// <param name="catalogInstanceId">The catalog instance this host is.</param>
    public InProcessAssetMutationBatchPublisher(
        IIntegrationMessageSerializer serializer, IAssetMutationBatchApplier applier, string catalogInstanceId)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogInstanceId);

        _serializer = serializer;
        _applier = applier;
        _catalogInstanceId = catalogInstanceId;
    }

    public Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.MessageType, IntegrationEventTypes.AssetMutationBatchRequestedV1, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"{nameof(InProcessAssetMutationBatchPublisher)} only applies '{IntegrationEventTypes.AssetMutationBatchRequestedV1}' messages, got '{envelope.MessageType}'.");
        }

        var batch = _serializer.Deserialize<AssetMutationBatchRequestedEvent>(envelope.Payload);
        if (!string.Equals(batch.CatalogInstanceId, _catalogInstanceId, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Asset mutation batch '{batch.BatchId}' is addressed to catalog instance '{batch.CatalogInstanceId}'; this host is '{_catalogInstanceId}'.");
        }

        return _applier.ApplyAsync(batch, ct);
    }

    public async Task PublishBatchAsync(IReadOnlyList<IntegrationEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        foreach (var envelope in envelopes)
        {
            await PublishAsync(envelope, ct).ConfigureAwait(false);
        }
    }
}
