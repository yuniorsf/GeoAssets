using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// In-memory <see cref="IGeoCoreClient"/> for the XD01-386 tests: a feature store plus a directed
/// graph of neighbor ids, recording every call. <see cref="OnCall"/> runs before each call so a
/// test can throw (transport failure) or block (timeout / cancellation).
/// </summary>
internal sealed class FakeGeoCoreClient : IGeoCoreClient
{
    public Dictionary<string, GeoFeature> Features { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<string>> Neighbors { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, a feature id with no stored feature is materialized by this factory.</summary>
    public Func<string, GeoFeature?>? FeatureFactory { get; set; }

    public Func<CancellationToken, Task>? OnCall { get; set; }

    public int GetFeatureCalls { get; private set; }

    public List<IReadOnlyList<string>> NeighborBatches { get; } = [];

    public List<AssetQuery> Queries { get; } = [];

    public int TotalCalls => GetFeatureCalls + NeighborBatches.Count + Queries.Count;

    public Task<AssetBatchAppliedEvent> ExecuteAssetDeltaBatchAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public async Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default)
    {
        GetFeatureCalls++;
        await BeforeCallAsync(ct);
        return Resolve(id);
    }

    public async Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default)
    {
        Queries.Add(query);
        await BeforeCallAsync(ct);

        IEnumerable<GeoFeature> matches;
        if (query.FeatureIds is { } ids)
            matches = ids.Select(Resolve).OfType<GeoFeature>();
        else
            matches = Features.Values
                .Where(f => query.AssetTypeId is null || string.Equals(f.Properties.AssetTypeId, query.AssetTypeId, StringComparison.Ordinal))
                .OrderBy(f => f.Id, StringComparer.Ordinal);

        var all = matches.ToList();
        return new PagedResult<GeoFeature> { Items = [.. all.Skip(query.Skip).Take(query.Take)], TotalCount = all.Count };
    }

    public Task<IReadOnlyList<string>> GetNeighborIdsAsync(string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(
        IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default)
    {
        NeighborBatches.Add(featureIds);
        await BeforeCallAsync(ct);

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var id in featureIds)
        {
            if (Neighbors.TryGetValue(id, out var targets))
                result[id] = targets;
        }

        return result;
    }

    private GeoFeature? Resolve(string id) =>
        Features.TryGetValue(id, out var feature) ? feature : FeatureFactory?.Invoke(id);

    private Task BeforeCallAsync(CancellationToken ct) => OnCall?.Invoke(ct) ?? Task.CompletedTask;
}
