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
    public const string FeatureCatalogMutatedV1 = "geoassets.assets.feature-catalog-mutated.v1";
    public const string TopologyInvalidatedV1 = "geoassets.assets.topology-invalidated.v1";
}
