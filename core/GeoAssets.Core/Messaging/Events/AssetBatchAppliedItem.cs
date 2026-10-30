namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Per-feature outcome record within an <see cref="AssetBatchAppliedEvent"/>.
/// </summary>
public sealed record AssetBatchAppliedItem(
    string FeatureId,
    AssetMutationKind MutationKind,
    string AssetTypeId,
    byte[] RowVersion);
