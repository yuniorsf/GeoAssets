namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Classifies the catalog mutation operation requested or applied to a geographic asset feature.
/// </summary>
public enum AssetMutationKind
{
    Unspecified = 0,
    UpsertFeature = 1,
    UpdateAttributes = 2,
    AddTopologyEdge = 3,
    RemoveTopologyEdge = 4,
    Decommission = 5,
}
