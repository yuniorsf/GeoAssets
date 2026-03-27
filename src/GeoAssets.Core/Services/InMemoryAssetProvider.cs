using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

public sealed class InMemoryAssetProvider : IAssetProvider
{
    private readonly Dictionary<string, GeoFeature> _features = [];
    private readonly List<AssetType> _assetTypes = [.. AssetType.Defaults];

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged;

    public GeoFeature? GetById(string id) =>
        _features.TryGetValue(id, out var f) ? f : null;

    public IReadOnlyList<GeoFeature> GetAll() =>
        [.. _features.Values];

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) =>
        [.. _features.Values.Where(f => f.Properties.AssetTypeId == assetTypeId)];

    public IReadOnlyList<GeoFeature> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();

        var lower = query.ToLowerInvariant();
        return [.. _features.Values.Where(f =>
            f.Properties.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.Description.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.CustomAttributes.Any(kv =>
                kv.Key.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains(lower, StringComparison.OrdinalIgnoreCase)))];
    }

    public void Add(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        FeatureAdded?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(GeoFeature feature)
    {
        feature.Properties.UpdatedAt = DateTime.UtcNow;
        _features[feature.Id] = feature;
        FeatureUpdated?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        foreach (var feature in features)
        {
            if (_features.ContainsKey(feature.Id))
                feature.Properties.UpdatedAt = DateTime.UtcNow;
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
        foreach (var f in features)
            _features[f.Id] = f;
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
        if (type is not null)
            _assetTypes.Remove(type);
    }

    // ── Spatial queries ───────────────────────────────────────────────────────

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Within(bounds))];

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Intersects(geometry))];

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

    public bool HasCycles() =>
        TopoGraph.HasCycles(_features.Values);

    public IReadOnlyList<GeoFeature> TopologicalSort() =>
        TopoGraph.TopologicalSort(_features.Values);
}
