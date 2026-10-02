using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using NetTopologySuite.Index.Strtree;

namespace GeoAssets.Provider.Shapefile;

/// <summary>
/// Full <see cref="IAssetProvider"/> implementation returned by
/// <see cref="ShapefileProviderPlugin"/> as the live, session-long asset store for an imported
/// shapefile — unlike the Rest/WFS providers' private caches, this is the entire provider a
/// user interacts with (map, forms, edits) for the rest of the session, so it needs the full
/// interface surface, not a purpose-sized subset. Replaces the shared, general-purpose
/// <c>InMemoryAssetProvider</c> (removed in XD01-131); logic here is otherwise identical to it.
/// </summary>
internal sealed class ShapefileFeatureStore : IAssetProvider, ISyncProvider
{
    private readonly Dictionary<string, GeoFeature> _features = [];
    private readonly List<AssetType> _assetTypes = [.. AssetType.Defaults];
    private readonly List<Layer> _layers = [];
    private readonly List<LayerRule> _layerRules = [];

    // STRtree is immutable once queried (Query() builds it), so a mutation invalidates it rather
    // than updating it in place — rebuilt lazily on the next spatial query.
    private STRtree<GeoFeature>? _spatialIndex;

    // Serializing a feature with tens of thousands of vertices is the dominant per-call cost on a
    // pan that re-hits it (far more than the spatial predicate) — cached per feature ID so the
    // same unchanged geometry isn't re-serialized on every overlapping pan, only invalidated here
    // alongside the spatial index.
    private readonly Dictionary<string, JsonElement> _jsonCache = [];

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged;

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

    public void Add(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        _spatialIndex = null;
        _jsonCache.Remove(feature.Id);
        FeatureAdded?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(GeoFeature feature)
    {
        feature.Properties.UpdatedAt = TimeProvider.System.GetUtcNow().UtcDateTime;
        _features[feature.Id] = feature;
        _spatialIndex = null;
        _jsonCache.Remove(feature.Id);
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
            _jsonCache.Remove(feature.Id);
        }
        _spatialIndex = null;
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        if (_features.Remove(id))
        {
            _spatialIndex = null;
            _jsonCache.Remove(id);
            FeatureDeleted?.Invoke(this, id);
            CollectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        _features.Clear();
        _spatialIndex = null;
        _jsonCache.Clear();
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadAll(IEnumerable<GeoFeature> features)
    {
        _features.Clear();
        foreach (var f in features) _features[f.Id] = f;
        _spatialIndex = null;
        _jsonCache.Clear();
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

    /// <summary>
    /// Builds (or reuses) an envelope-based <see cref="STRtree{T}"/> over every feature with a
    /// geometry, so <see cref="GetWithin"/>/<see cref="GetIntersecting"/> only run the full
    /// topological predicate against envelope candidates instead of every feature in the store —
    /// a flat per-call cost regardless of how many of the store's features are actually nearby.
    /// </summary>
    private STRtree<GeoFeature> EnsureSpatialIndex()
    {
        if (_spatialIndex is not null) return _spatialIndex;

        var tree = new STRtree<GeoFeature>();
        foreach (var f in _features.Values)
        {
            if (f.Geometry is null) continue;
            tree.Insert(f.Geometry.NtsGeometry.EnvelopeInternal, f);
        }
        return _spatialIndex = tree;
    }

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) =>
        [.. EnsureSpatialIndex().Query(bounds.NtsGeometry.EnvelopeInternal)
            .Where(f => f.Geometry is not null && f.Geometry.Within(bounds))];

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) =>
        [.. EnsureSpatialIndex().Query(geometry.NtsGeometry.EnvelopeInternal)
            .Where(f => f.Geometry is not null && f.Geometry.Intersects(geometry))];

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
        var opts = GeoJsonSerializer.GetCompactOptions();

        var result = new List<JsonElement>(features.Count);
        foreach (var f in features)
        {
            if (!_jsonCache.TryGetValue(f.Id, out var element))
                _jsonCache[f.Id] = element = JsonSerializer.SerializeToElement(f, opts);
            result.Add(element);
        }
        return result;
    }

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        [.. _features.Values
            .Where(f => f.Geometry is not null && f.Geometry.Distance(center) <= distanceDegrees)
            .OrderBy(f => f.Geometry!.Distance(center))];
}
