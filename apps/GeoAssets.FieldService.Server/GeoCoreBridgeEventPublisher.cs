using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// <see cref="IIntegrationEventPublisher"/> that bridges
/// <c>GeoAssets.Workflow.Persistence.Outbox.OutboxRelayProcessor</c>'s claimed outbox batches
/// (XD01-229) directly to the Core GeoAssets Server over HTTP via
/// <see cref="IGeoCoreClient.ExecuteAssetDeltaBatchAsync"/>, rather than a real message broker.
/// This only ever publishes
/// <see cref="IntegrationEventTypes.AssetMutationBatchRequestedV1"/> envelopes — the only message
/// type this host's outbox stages (<c>InspectionCommitService</c>, XD01-228) — and treats the HTTP
/// call itself as "the publish": the retry/backoff/at-least-once semantics
/// <see cref="IIntegrationEventPublisher"/>'s contract promises come entirely from
/// <c>OutboxRelayProcessor</c>'s own claim/lease loop, not from a broker.
/// </summary>
internal sealed class GeoCoreBridgeEventPublisher(
    IGeoCoreClientResolver clientResolver,
    IIntegrationMessageSerializer serializer) : IIntegrationEventPublisher
{
    public Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.MessageType, IntegrationEventTypes.AssetMutationBatchRequestedV1, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"{nameof(GeoCoreBridgeEventPublisher)} only publishes '{IntegrationEventTypes.AssetMutationBatchRequestedV1}' messages, got '{envelope.MessageType}'.");
        }

        var batch = serializer.Deserialize<AssetMutationBatchRequestedEvent>(envelope.Payload);
        var client = clientResolver.Resolve(batch.CatalogInstanceId, batch.OrganizationId);
        return client.ExecuteAssetDeltaBatchAsync(batch, ct);
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
