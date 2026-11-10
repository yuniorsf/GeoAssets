using GeoAssets.Core.Erp.Maximo;
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
///
/// After the Core GIS mutation succeeds, also offers the same batch to
/// <see cref="MaximoAssetLifecycleSyncService"/> (XD01-232) — a no-op for every batch except an
/// asset swap's. <paramref name="maximoSync"/> defaults to <c>null</c> so existing 2-arg
/// construction (tests, before XD01-232) keeps compiling; the host wires the real instance via DI.
/// </summary>
internal sealed class GeoCoreBridgeEventPublisher(
    IGeoCoreClientResolver clientResolver,
    IIntegrationMessageSerializer serializer,
    MaximoAssetLifecycleSyncService? maximoSync = null) : IIntegrationEventPublisher
{
    public async Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.MessageType, IntegrationEventTypes.AssetMutationBatchRequestedV1, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"{nameof(GeoCoreBridgeEventPublisher)} only publishes '{IntegrationEventTypes.AssetMutationBatchRequestedV1}' messages, got '{envelope.MessageType}'.");
        }

        var batch = serializer.Deserialize<AssetMutationBatchRequestedEvent>(envelope.Payload);
        var client = clientResolver.Resolve(batch.CatalogInstanceId, batch.OrganizationId);
        await client.ExecuteAssetDeltaBatchAsync(batch, ct).ConfigureAwait(false);

        if (maximoSync is not null)
        {
            await maximoSync.SyncIfSwapAsync(batch, ct).ConfigureAwait(false);
        }
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
