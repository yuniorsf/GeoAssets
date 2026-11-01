using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Core.Services;

/// <summary>
/// Combines <see cref="AssetMutationBatchApplier"/> with <see cref="IAppliedMutationBatchStore"/>'s
/// idempotency check (XD01-229): a batch whose <see cref="AssetMutationBatchRequestedEvent.BatchId"/>
/// was already applied returns the original result without re-applying; otherwise applies it and
/// records the result first. What the Core GeoAssets Server's <c>/api/geoassets/mutation-batches</c>
/// endpoint actually calls.
/// </summary>
public sealed class IdempotentMutationBatchApplier(
    IAssetProvider provider,
    IAppliedMutationBatchStore store,
    TimeProvider timeProvider)
{
    public async Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var existing = await store.FindAsync(batch.BatchId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var applied = new AssetMutationBatchApplier(provider, timeProvider).Apply(batch);
        await store.RecordAsync(applied, ct).ConfigureAwait(false);
        return applied;
    }
}
