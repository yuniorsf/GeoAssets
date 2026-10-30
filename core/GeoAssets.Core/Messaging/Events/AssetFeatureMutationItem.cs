namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Describes a single feature mutation operation within an <see cref="AssetMutationBatchRequestedEvent"/>.
/// </summary>
public sealed record AssetFeatureMutationItem(
    string FeatureId,
    AssetMutationKind MutationKind,
    string Name,
    string AssetTypeId,
    string Description,
    string LayerId,
    AssetGeometrySnapshot? Geometry,
    IReadOnlyList<TopoEdgeSnapshot> TopologyEdges,
    IReadOnlyDictionary<string, string> CustomAttributes,
    byte[]? ExpectedRowVersion = null);
