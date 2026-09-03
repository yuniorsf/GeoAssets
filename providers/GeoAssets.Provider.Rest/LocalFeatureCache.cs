using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// Local dictionary-backed cache <see cref="RestAssetProvider"/> reads from and writes through
/// to, sized to exactly the surface it uses — replaces the shared, general-purpose
/// <c>InMemoryAssetProvider</c> (removed in XD01-131) as a private implementation detail of
/// this class only. Not thread-safe, matching the removed type's own guarantees (or lack
/// thereof) — callers were never relying on concurrent access.
/// </summary>
internal sealed class LocalFeatureCache
{
    private readonly Dictionary<string, GeoFeature> _features = [];
    private readonly List<AssetType> _assetTypes = [.. AssetType.Defaults];
    private readonly List<Layer> _layers = [];
    private readonly List<LayerRule> _layerRules = [];

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged;

    // ── Reads ─────────────────────────────────────────────────────────────────

    public GeoFeature? GetById(string id) =>
        _features.TryGetValue(id, out var f) ? f : null;

    public IReadOnlyList<GeoFeature> GetAll() => [.. _features.Values];

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

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Within(bounds))];

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Intersects(geometry))];

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        [.. _features.Values
            .Where(f => f.Geometry is not null && f.Geometry.Distance(center) <= distanceDegrees)
            .OrderBy(f => f.Geometry!.Distance(center))];

    // ── Asset types / layers ─────────────────────────────────────────────────

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

    // ── Writes ────────────────────────────────────────────────────────────────

    public void Add(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        FeatureAdded?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(GeoFeature feature)
    {
        feature.Properties.UpdatedAt = TimeProvider.System.GetUtcNow().UtcDateTime;
        _features[feature.Id] = feature;
        FeatureUpdated?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        foreach (var feature in features)
        {
            if (_features.ContainsKey(feature.Id))
                feature.Properties.UpdatedAt = TimeProvider.System.GetUtcNow().UtcDateTime;
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
}
