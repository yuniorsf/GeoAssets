using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Core.Erp.Maximo;

/// <summary>
/// Recognizes an asset-swap completion inside an <see cref="AssetMutationBatchRequestedEvent"/>:
/// exactly one <see cref="AssetMutationKind.Decommission"/> item (the old serial leaving service)
/// paired with exactly one <see cref="AssetMutationKind.UpsertFeature"/> item (the new serial
/// activating at the target location), both carrying <see cref="SerialNumberAttributeKey"/> in
/// <see cref="AssetFeatureMutationItem.CustomAttributes"/>. Any other shape — zero or multiple
/// Decommission items, no matching new-asset item, a missing serial — means this batch is not a
/// swap, and callers must not dispatch to Maximo for it (XD01-232's AC1: "exactly one Maximo
/// state update per serial pair", which only applies when there is one).
/// </summary>
public static class MaximoAssetSwapExtractor
{
    public const string SerialNumberAttributeKey = "serialNumber";

    public static MaximoAssetLifecycleUpdate? TryExtractSwap(AssetMutationBatchRequestedEvent batch, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(timeProvider);

        AssetFeatureMutationItem? oldItem = null;
        AssetFeatureMutationItem? newItem = null;

        foreach (var item in batch.Mutations)
        {
            if (item.MutationKind == AssetMutationKind.Decommission)
            {
                if (oldItem is not null)
                {
                    return null; // more than one decommission in the batch — not a single swap pair.
                }

                oldItem = item;
            }
            else if (item.MutationKind == AssetMutationKind.UpsertFeature)
            {
                if (newItem is not null)
                {
                    return null; // more than one upsert — ambiguous, not a single swap pair.
                }

                newItem = item;
            }
        }

        if (oldItem is null || newItem is null)
        {
            return null;
        }

        if (!oldItem.CustomAttributes.TryGetValue(SerialNumberAttributeKey, out var oldSerial) || string.IsNullOrWhiteSpace(oldSerial))
        {
            return null;
        }

        if (!newItem.CustomAttributes.TryGetValue(SerialNumberAttributeKey, out var newSerial) || string.IsNullOrWhiteSpace(newSerial))
        {
            return null;
        }

        return new MaximoAssetLifecycleUpdate(
            BatchId: batch.BatchId,
            OldSerial: oldSerial,
            NewSerial: newSerial,
            LocationFeatureId: newItem.FeatureId,
            OrganizationId: batch.OrganizationId,
            SourceOrderId: batch.SourceOrderId,
            SyncedAtUtc: timeProvider.GetUtcNow());
    }
}
