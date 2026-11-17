using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.TestKit;

/// <summary>An asset mutation batch as staged on the outbox, with the envelope members an approval sets.</summary>
public sealed record StagedAssetMutationBatch(
    string MessageType, string PartitionKey, string? CorrelationId, string? CausationId, AssetMutationBatchRequestedEvent Batch);
