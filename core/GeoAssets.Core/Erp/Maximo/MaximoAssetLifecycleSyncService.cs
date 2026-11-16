using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Core.Erp.Maximo;

/// <summary>
/// Dispatches an asset-swap completion to IBM Maximo MIF exactly once per
/// <see cref="AssetMutationBatchRequestedEvent.BatchId"/> (XD01-232's AC1). Idempotency is
/// check-then-act via <see cref="IIdempotentInbox"/>, recorded only *after* a successful
/// <see cref="IMaximoAssetLifecycleDispatcher.DispatchAsync"/> call — so a failed dispatch (AC2:
/// "failure to reach Maximo retries without corrupting local ledger/outbox state") leaves nothing
/// recorded and a later redelivery of the same batch retries the dispatch, while a successful one
/// is never repeated. Same shape as <see cref="Services.IdempotentMutationBatchApplier"/>'s own
/// find-then-record pattern; safe because this host's outbox relay delivers one claimed message
/// at a time, never concurrently, for the same <c>BatchId</c>.
/// </summary>
public sealed class MaximoAssetLifecycleSyncService(
    IMaximoAssetLifecycleDispatcher dispatcher,
    IIdempotentInbox idempotentInbox,
    TimeProvider timeProvider)
{
    public const string ConsumerName = "MaximoAssetLifecycleSync";
    internal const string MessageType = "maximo.asset-lifecycle-swap.v1";
    private static readonly TimeSpan DedupRetention = TimeSpan.FromDays(30);

    public async Task<MaximoSyncOutcome> SyncIfSwapAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var update = MaximoAssetSwapExtractor.TryExtractSwap(batch, timeProvider);
        if (update is null)
        {
            return MaximoSyncOutcome.NotASwap;
        }

        if (await idempotentInbox.HasProcessedAsync(batch.BatchId, ConsumerName, ct).ConfigureAwait(false))
        {
            return MaximoSyncOutcome.AlreadyDispatched;
        }

        await dispatcher.DispatchAsync(update, ct).ConfigureAwait(false);

        await idempotentInbox.TryRecordProcessedAsync(batch.BatchId, ConsumerName, MessageType, DedupRetention, ct).ConfigureAwait(false);
        return MaximoSyncOutcome.Dispatched;
    }
}
