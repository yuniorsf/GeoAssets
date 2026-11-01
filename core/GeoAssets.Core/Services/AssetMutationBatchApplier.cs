using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Applies an <see cref="AssetMutationBatchRequestedEvent"/>'s feature mutations onto an
/// <see cref="IAssetProvider"/> (XD01-229) — the actual apply logic the Core GeoAssets Server's
/// <c>/api/geoassets/mutation-batches</c> endpoint runs once a batch has passed its idempotency
/// check (see that endpoint's own notes on why idempotency is <c>BatchId</c>-keyed rather than
/// per-item optimistic concurrency: <see cref="AssetFeatureMutationItem.ExpectedRowVersion"/> has
/// no corresponding concurrency token on <see cref="GeoFeature"/>/<c>GeoEntityRow</c> yet).
///
/// <see cref="TopoGraph"/> needs no explicit invalidation after this runs — it is a stateless set
/// of algorithms over whatever <see cref="IAssetProvider.GetAll"/> returns at call time, so the
/// very next call after <see cref="Apply"/> already reflects every mutation applied here.
/// </summary>
public sealed class AssetMutationBatchApplier(IAssetProvider provider, TimeProvider timeProvider)
{
    public AssetBatchAppliedEvent Apply(AssetMutationBatchRequestedEvent batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var appliedItems = new List<AssetBatchAppliedItem>(batch.Mutations.Count);
        foreach (var item in batch.Mutations)
        {
            appliedItems.Add(ApplyItem(item));
        }

        return AssetBatchAppliedEvent.Create(
            batchId: batch.BatchId,
            catalogInstanceId: batch.CatalogInstanceId,
            organizationId: batch.OrganizationId,
            requestedByService: batch.RequestedByService,
            appliedByUserId: batch.PerformedByUserId,
            appliedItems: appliedItems,
            timeProvider: timeProvider,
            sourceOrderId: batch.SourceOrderId,
            sourceOrderTypeId: batch.SourceOrderTypeId,
            scopeId: batch.ScopeId,
            correlationId: batch.CorrelationId);
    }

    private AssetBatchAppliedItem ApplyItem(AssetFeatureMutationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.MutationKind switch
        {
            AssetMutationKind.UpsertFeature => ApplyUpsert(item),
            AssetMutationKind.UpdateAttributes => ApplyUpdateAttributes(item),
            AssetMutationKind.AddTopologyEdge => ApplyTopologyEdge(item, add: true),
            AssetMutationKind.RemoveTopologyEdge => ApplyTopologyEdge(item, add: false),
            AssetMutationKind.Decommission => ApplyDecommission(item),
            _ => throw new NotSupportedException($"Unsupported mutation kind '{item.MutationKind}'."),
        };
    }

    private AssetBatchAppliedItem ApplyUpsert(AssetFeatureMutationItem item)
    {
        var existing = provider.GetById(item.FeatureId);
        var feature = existing ?? new GeoFeature { Id = item.FeatureId };

        feature.Properties.Name = item.Name;
        feature.Properties.AssetTypeId = item.AssetTypeId;
        feature.Properties.Description = item.Description;
        feature.Properties.LayerId = item.LayerId;
        feature.Properties.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            feature.Properties.CreatedAt = feature.Properties.UpdatedAt;
        }

        foreach (var (key, value) in item.CustomAttributes)
        {
            feature.Properties.CustomAttributes[key] = value;
        }

        if (item.Geometry is not null)
        {
            feature.Geometry = ToGeoGeometry(item.Geometry);
        }

        ReplaceTopologyEdges(feature, item.TopologyEdges);

        if (existing is null)
        {
            provider.Add(feature);
        }
        else
        {
            provider.Update(feature);
        }

        return new AssetBatchAppliedItem(item.FeatureId, item.MutationKind, item.AssetTypeId, RowVersion: []);
    }

    private AssetBatchAppliedItem ApplyUpdateAttributes(AssetFeatureMutationItem item)
    {
        var feature = provider.GetById(item.FeatureId)
            ?? throw new KeyNotFoundException($"Feature '{item.FeatureId}' not found for UpdateAttributes.");

        foreach (var (key, value) in item.CustomAttributes)
        {
            feature.Properties.CustomAttributes[key] = value;
        }

        feature.Properties.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        provider.Update(feature);

        return new AssetBatchAppliedItem(item.FeatureId, item.MutationKind, feature.Properties.AssetTypeId, RowVersion: []);
    }

    private AssetBatchAppliedItem ApplyTopologyEdge(AssetFeatureMutationItem item, bool add)
    {
        var feature = provider.GetById(item.FeatureId)
            ?? throw new KeyNotFoundException($"Feature '{item.FeatureId}' not found for {item.MutationKind}.");

        foreach (var edge in item.TopologyEdges)
        {
            feature.Topology.RemoveAll(e => string.Equals(e.TargetId, edge.TargetId, StringComparison.Ordinal)
                && string.Equals(e.Kind, edge.Kind, StringComparison.Ordinal));

            if (add)
            {
                feature.Topology.Add(new TopoEdge
                {
                    TargetId = edge.TargetId,
                    Kind = edge.Kind,
                    Weight = edge.Weight,
                    Metadata = edge.Metadata is null
                        ? new Dictionary<string, string>(StringComparer.Ordinal)
                        : new Dictionary<string, string>(edge.Metadata, StringComparer.Ordinal),
                });
            }
        }

        feature.Properties.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        provider.Update(feature);

        return new AssetBatchAppliedItem(item.FeatureId, item.MutationKind, feature.Properties.AssetTypeId, RowVersion: []);
    }

    private AssetBatchAppliedItem ApplyDecommission(AssetFeatureMutationItem item)
    {
        var feature = provider.GetById(item.FeatureId);
        provider.Delete(item.FeatureId);
        return new AssetBatchAppliedItem(item.FeatureId, item.MutationKind, feature?.Properties.AssetTypeId ?? item.AssetTypeId, RowVersion: []);
    }

    private static void ReplaceTopologyEdges(GeoFeature feature, IReadOnlyList<TopoEdgeSnapshot> edges)
    {
        if (edges.Count == 0)
        {
            return;
        }

        feature.Topology.Clear();
        foreach (var edge in edges)
        {
            feature.Topology.Add(new TopoEdge
            {
                TargetId = edge.TargetId,
                Kind = edge.Kind,
                Weight = edge.Weight,
                Metadata = edge.Metadata is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(edge.Metadata, StringComparer.Ordinal),
            });
        }
    }

    private static GeoGeometry ToGeoGeometry(AssetGeometrySnapshot snapshot) => snapshot.Kind switch
    {
        WireGeometryKind.Point => new GeoPoint(snapshot.PointCoordinates[0], snapshot.PointCoordinates[1], snapshot.Srid),
        WireGeometryKind.LineString => new GeoLineString(
            snapshot.LineCoordinates.Select(p => (p[0], p[1])), snapshot.Srid),
        WireGeometryKind.Polygon => new GeoPolygon(
            snapshot.PolygonRings[0].Select(p => (p[0], p[1])),
            snapshot.PolygonRings.Skip(1).Select(ring => ring.Select(p => (p[0], p[1])))),
        _ => throw new NotSupportedException($"Unsupported geometry kind '{snapshot.Kind}'."),
    };
}
