using GeoAssets.Core.Interfaces;
using GeoAssets.IpBilling.AssetTypes;

namespace GeoAssets.IpBilling.Resolution;

/// <summary>
/// Resolves the <c>Poste (IP)</c> <c>GeoFeature.Id</c>s fed by a transformer by querying the
/// Core GeoAssets Server's topology API (<see cref="IGeoCoreClient"/>) along
/// <c>TopoEdge(Kind="feeds")</c>.
/// </summary>
public sealed class HttpPoleResolver(IGeoCoreClient coreClient) : IPoleResolver
{
    public const string FeedsEdgeKind = "feeds";
    private static readonly string PoleAssetTypeId = IpBillingAssetTypes.Poste.Id.ToString();

    public async Task<IReadOnlyList<Guid>> ResolvePoleFeatureIdsAsync(
        Guid transformerFeatureId, CancellationToken ct = default)
    {
        var neighborIds = await coreClient.GetNeighborIdsAsync(
            featureId: transformerFeatureId.ToString(),
            edgeKind: FeedsEdgeKind,
            targetAssetTypeId: PoleAssetTypeId,
            ct: ct).ConfigureAwait(false);

        return ParseGuids(neighborIds);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ResolvePoleFeatureIdsBatchAsync(
        IReadOnlyList<Guid> transformerFeatureIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transformerFeatureIds);
        if (transformerFeatureIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();

        var stringIds = transformerFeatureIds.Select(id => id.ToString()).Distinct(StringComparer.Ordinal).ToList();
        var batch = await coreClient.GetNeighborIdsBatchAsync(
            featureIds: stringIds,
            edgeKind: FeedsEdgeKind,
            targetAssetTypeId: PoleAssetTypeId,
            ct: ct).ConfigureAwait(false);

        var result = new Dictionary<Guid, IReadOnlyList<Guid>>(transformerFeatureIds.Count);
        foreach (var id in transformerFeatureIds)
        {
            result[id] = batch.TryGetValue(id.ToString(), out var neighborIds)
                ? ParseGuids(neighborIds)
                : [];
        }

        return result;
    }

    private static List<Guid> ParseGuids(IReadOnlyList<string> rawIds)
    {
        var list = new List<Guid>(rawIds.Count);
        foreach (var raw in rawIds)
        {
            if (Guid.TryParse(raw, out var guid))
                list.Add(guid);
        }
        return list;
    }
}

