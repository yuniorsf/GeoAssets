using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.Integrations.Scada;

namespace GeoAssets.Workflow.Tests;

/// <summary>
/// Full, faithful <see cref="IAssetProvider"/> test double — replaces the shared,
/// general-purpose <c>InMemoryAssetProvider</c> (removed in XD01-131) as this project's fixture
/// for "a real working store", used wherever a test needs genuine add/query/topology behavior
/// rather than a stub. Topology queries delegate to the real <see cref="TopoGraph"/> static
/// helper (unaffected by the removal), the same way the deleted class did. Also answers the
/// narrow asset-read ports (XD01-386) in process, so a test hands the same store to a guard or
/// service that now depends on a port instead of <see cref="IAssetProvider"/>.
/// </summary>
internal sealed class TestAssetProvider : IAssetProvider, IAssetProximityQuery, IAssetTopologyQuery
{
    private readonly Dictionary<string, GeoFeature> _features = [];
    private readonly List<AssetType> _assetTypes = [.. AssetType.Defaults];
    private readonly List<Layer> _layers = [];
    private readonly List<LayerRule> _layerRules = [];
    private readonly TimeProvider _timeProvider;

    public TestAssetProvider(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged;

    public GeoFeature? GetById(string id) =>
        _features.TryGetValue(id, out var f) ? f : null;

    public IReadOnlyList<GeoFeature> GetAll() => [.. _features.Values];

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) =>
        [.. _features.Values.Where(f => string.Equals(f.Properties.AssetTypeId, assetTypeId, StringComparison.Ordinal))];

    public IReadOnlyList<GeoFeature> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();

        return [.. _features.Values.Where(f =>
            f.Properties.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.CustomAttributes.Any(kv =>
                kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))];
    }

    public void Add(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        FeatureAdded?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(GeoFeature feature)
    {
        feature.Properties.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        _features[feature.Id] = feature;
        FeatureUpdated?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        foreach (var feature in features)
        {
            if (_features.ContainsKey(feature.Id))
                feature.Properties.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            _features[feature.Id] = feature;
        }
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

    public IReadOnlyList<AssetType> GetAssetTypes() => [.. _assetTypes];

    public void AddAssetType(AssetType assetType)
    {
        if (_assetTypes.All(t => t.Id != assetType.Id))
            _assetTypes.Add(assetType);
    }

    public void DeleteAssetType(Guid id)
    {
        var type = _assetTypes.FirstOrDefault(t => t.Id == id && !t.IsBuiltIn);
        if (type is not null) _assetTypes.Remove(type);
    }

    public IReadOnlyList<Layer> GetLayers() => [.. _layers];

    public void AddLayer(Layer layer)
    {
        if (_layers.All(l => l.Id != layer.Id)) _layers.Add(layer);
    }

    public void DeleteLayer(Guid id)
    {
        var layer = _layers.FirstOrDefault(l => l.Id == id);
        if (layer is not null) _layers.Remove(layer);
    }

    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) =>
        [.. _layerRules.Where(r => r.AssetTypeId == assetTypeId)];

    public void AddLayerRule(LayerRule layerRule)
    {
        if (_layerRules.All(r => r.Id != layerRule.Id)) _layerRules.Add(layerRule);
    }

    public void DeleteLayerRule(Guid id)
    {
        var rule = _layerRules.FirstOrDefault(r => r.Id == id);
        if (rule is not null) _layerRules.Remove(rule);
    }

    // ── Spatial queries ───────────────────────────────────────────────────────

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
        var features = await GetInBoundsAsync(minLon, minLat, maxLon, maxLat);
        var opts = GeoJsonSerializer.CompactOptions;
        return [.. features.Select(f => JsonSerializer.SerializeToElement(f, opts))];
    }

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        [.. _features.Values
            .Where(f => f.Geometry is not null && f.Geometry.Distance(center) <= distanceDegrees)
            .OrderBy(f => f.Geometry!.Distance(center))];

    // ── Topology queries ──────────────────────────────────────────────────────

    public IReadOnlyList<GeoFeature> GetNeighbors(string featureId) =>
        TopoGraph.GetNeighbors(featureId, _features.Values);

    public IReadOnlyList<GeoFeature> GetDescendants(string featureId) =>
        TopoGraph.GetDescendants(featureId, _features.Values);

    public IReadOnlyList<GeoFeature> GetAncestors(string featureId) =>
        TopoGraph.GetAncestors(featureId, _features.Values);

    public IReadOnlyList<GeoFeature> FindPath(string fromId, string toId) =>
        TopoGraph.FindPath(fromId, toId, _features.Values);

    public IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId) =>
        TopoGraph.FindShortestPath(fromId, toId, _features.Values);

    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents() =>
        TopoGraph.GetConnectedComponents(_features.Values);

    public bool HasCycles() => TopoGraph.HasCycles(_features.Values);

    public IReadOnlyList<GeoFeature> TopologicalSort() => TopoGraph.TopologicalSort(_features.Values);

    // ── Narrow asset-read ports (XD01-386) ────────────────────────────────────

    Task<bool> IAssetProximityQuery.IsWithinDistanceAsync(string featureId, GeoPoint point, double toleranceMeters, CancellationToken ct) =>
        ((IAssetProvider)this).IsWithinDistanceAsync(featureId, point, toleranceMeters);

    Task<GeoFeature?> IAssetTopologyQuery.GetFeatureAsync(string id, CancellationToken ct) =>
        Task.FromResult(GetById(id));

    Task<IReadOnlyList<GeoFeature>> IAssetTopologyQuery.GetDescendantsAsync(string id, string edgeKind, CancellationToken ct) =>
        Task.FromResult(((IAssetProvider)this).GetDescendants(id, edgeKind));

    Task<IReadOnlyList<GeoFeature>> IAssetTopologyQuery.GetFeaturesByAssetTypeAsync(string assetTypeId, CancellationToken ct) =>
        Task.FromResult(GetByAssetType(assetTypeId));
}
