using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Records which <see cref="AssetMutationBatchRequestedEvent.BatchId"/>s the Core GeoAssets
/// Server has already applied, so a redelivered batch (at-least-once outbox delivery, XD01-229)
/// returns the original <see cref="AssetBatchAppliedEvent"/> instead of re-applying the
/// mutations. <c>BatchId</c>-keyed rather than per-item optimistic concurrency — see
/// <see cref="Services.AssetMutationBatchApplier"/>'s own doc comment for why.
/// </summary>
public interface IAppliedMutationBatchStore
{
    Task<AssetBatchAppliedEvent?> FindAsync(Guid batchId, CancellationToken ct = default);

    Task RecordAsync(AssetBatchAppliedEvent applied, CancellationToken ct = default);
}
