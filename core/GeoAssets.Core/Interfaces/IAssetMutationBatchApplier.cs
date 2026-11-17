using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Applies an <see cref="AssetMutationBatchRequestedEvent"/> to the asset catalog of the batch's
/// own <see cref="AssetMutationBatchRequestedEvent.OrganizationId"/> (XD01-337): every mutation
/// and the record that the batch was applied commit together or not at all, and a batch whose
/// <see cref="AssetMutationBatchRequestedEvent.BatchId"/> was already applied is not applied again
/// — the original <see cref="AssetBatchAppliedEvent"/> is returned instead. Any failure (a feature
/// that does not exist in that organization, a validation error, the database) leaves the catalog
/// unchanged and surfaces as an exception, so the caller's delivery can be retried.
/// </summary>
public interface IAssetMutationBatchApplier
{
    Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct);
}
