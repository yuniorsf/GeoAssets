using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Workflow.Agents.Tests.Dispatch;

/// <summary>Minimal in-memory <see cref="IAssetProvider"/> test double for XD01-240's dispatch
/// tests — same shape as the identically-named fixtures in sibling test projects
/// (e.g. <c>GeoAssets.Workflow.Tests.TestAssetProvider</c>).</summary>
internal sealed class TestAssetProvider : IAssetProvider
{
    private readonly Dictionary<string, GeoFeature> _features = [];

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged;

    public GeoFeature? GetById(string id) => _features.TryGetValue(id, out var f) ? f : null;

    public IReadOnlyList<GeoFeature> GetAll() => [.. _features.Values];

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) =>
        [.. _features.Values.Where(f => string.Equals(f.Properties.AssetTypeId, assetTypeId, StringComparison.Ordinal))];

    public IReadOnlyList<GeoFeature> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();
        return [.. _features.Values.Where(f => f.Properties.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];
    }

    public void Add(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        FeatureAdded?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        FeatureUpdated?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        if (_features.Remove(id))
        {
            FeatureDeleted?.Invoke(this, id);
            CollectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        foreach (var feature in features) _features[feature.Id] = feature;
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _features.Clear();
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadAll(IEnumerable<GeoFeature> features)
    {
        _features.Clear();
        foreach (var f in features) _features[f.Id] = f;
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<AssetType> GetAssetTypes() => [.. AssetType.Defaults];
    public void AddAssetType(AssetType assetType) { }
    public void DeleteAssetType(Guid id) { }
    public IReadOnlyList<Layer> GetLayers() => [];
    public void AddLayer(Layer layer) { }
    public void DeleteLayer(Guid id) { }
    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => [];
    public void AddLayerRule(LayerRule layerRule) { }
    public void DeleteLayerRule(Guid id) { }

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Within(bounds))];

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Intersects(geometry))];

    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        var bbox = new GeoPolygon([
            (minLon, minLat), (maxLon, minLat), (maxLon, maxLat), (minLon, maxLat), (minLon, minLat)
        ]);
        return Task.FromResult(GetIntersecting(bbox));
    }

    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        var features = await GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
        return [.. features.Select(f => JsonSerializer.SerializeToElement(f, GeoJsonSerializer.CompactOptions))];
    }

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        [.. _features.Values
            .Where(f => f.Geometry is not null && f.Geometry.Distance(center) <= distanceDegrees)
            .OrderBy(f => f.Geometry!.Distance(center))];

    public IReadOnlyList<GeoFeature> GetNeighbors(string featureId) => TopoGraph.GetNeighbors(featureId, _features.Values);
    public IReadOnlyList<GeoFeature> GetDescendants(string featureId) => TopoGraph.GetDescendants(featureId, _features.Values);
    public IReadOnlyList<GeoFeature> GetAncestors(string featureId) => TopoGraph.GetAncestors(featureId, _features.Values);
    public IReadOnlyList<GeoFeature> FindPath(string fromId, string toId) => TopoGraph.FindPath(fromId, toId, _features.Values);
    public IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId) => TopoGraph.FindShortestPath(fromId, toId, _features.Values);
    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents() => TopoGraph.GetConnectedComponents(_features.Values);
    public bool HasCycles() => TopoGraph.HasCycles(_features.Values);
    public IReadOnlyList<GeoFeature> TopologicalSort() => TopoGraph.TopologicalSort(_features.Values);
}
