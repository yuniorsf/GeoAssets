namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Canonical versioned message-type discriminators for GeoAssets cross-service integration events.
/// </summary>
public static class IntegrationEventTypes
{
    public const int CurrentSchemaVersion = 1;

    public const string OrderStateChangedV1 = "geoassets.workflow.order-state-changed.v1";
    public const string AssetMutationBatchRequestedV1 = "geoassets.assets.mutation-batch-requested.v1";
    public const string AssetBatchAppliedV1 = "geoassets.assets.batch-applied.v1";

    /// <summary>
    /// The kernel applied an <see cref="AssetMutationBatchRequestedV1"/> batch (XD01-383, the approval saga's success reply).
    /// The payload is <see cref="AssetBatchAppliedEvent"/> — the applier's own result, the same concept
    /// <see cref="AssetBatchAppliedV1"/> names, which no host publishes; the saga's reply has its own discriminator so its
    /// consumers never confuse it with anything else on a shared topic.
    /// </summary>
    public const string AssetMutationBatchAppliedV1 = "geoassets.assets.mutation-batch-applied.v1";

    /// <summary>
    /// The kernel refused an <see cref="AssetMutationBatchRequestedV1"/> batch for a business reason — a feature that is
    /// not in the organization's catalog, an invalid or unsupported mutation (XD01-383, the approval saga's compensating
    /// reply). The payload is <see cref="AssetMutationBatchRejectedEvent"/>. A transport or database failure is never a
    /// rejection: the batch is redelivered instead.
    /// </summary>
    public const string AssetMutationBatchRejectedV1 = "geoassets.assets.mutation-batch-rejected.v1";
    public const string FeatureCatalogMutatedV1 = "geoassets.assets.feature-catalog-mutated.v1";
    public const string TopologyInvalidatedV1 = "geoassets.assets.topology-invalidated.v1";

    /// <summary>The global target ledger reserved targets for a vertical's order (XD01-379).</summary>
    public const string TargetsReservedV1 = "geoassets.targets.targets-reserved.v1";

    /// <summary>The global target ledger released targets of a vertical's order (XD01-379).</summary>
    public const string TargetsReleasedV1 = "geoassets.targets.targets-released.v1";

    /// <summary>The global target ledger committed targets of a vertical's order (XD01-379).</summary>
    public const string TargetsCommittedV1 = "geoassets.targets.targets-committed.v1";
}
