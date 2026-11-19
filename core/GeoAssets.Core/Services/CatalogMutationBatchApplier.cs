using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Core.Services;

/// <summary>
/// The generic <see cref="IAssetMutationBatchApplier"/> (XD01-374): applies a batch to a catalog through
/// <see cref="AssetMutationBatchApplier"/> — idempotently through <see cref="IdempotentMutationBatchApplier"/> when an
/// <see cref="IAppliedMutationBatchStore"/> is given. What a host uses when its catalog's provider registers no applier of
/// its own (a pushdown such as the PostgreSQL one).
/// </summary>
public sealed class CatalogMutationBatchApplier(IAssetProvider catalog, IAppliedMutationBatchStore? store, TimeProvider timeProvider)
    : IAssetMutationBatchApplier
{
    public Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();

        return store is null
            ? Task.FromResult(new AssetMutationBatchApplier(catalog, timeProvider).Apply(batch))
            : new IdempotentMutationBatchApplier(catalog, store, timeProvider).ApplyAsync(batch, ct);
    }
}
