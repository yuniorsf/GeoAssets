using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Provider.WFS;

/// <summary>
/// Local dictionary-backed cache <see cref="WfsAssetProvider"/> reads from, sized to exactly
/// the surface it uses — replaces the shared, general-purpose <c>InMemoryAssetProvider</c>
/// (removed in XD01-131) as a private implementation detail of this class only. Read-only from
/// the outside (only <see cref="LoadAll"/> is ever called — <see cref="WfsAssetProvider"/> is
/// itself read-only, WFS-T being out of scope), unlike the Rest provider's equivalent cache.
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

    public IReadOnlyList<AssetType> GetAssetTypes() => [.. _assetTypes];

    public IReadOnlyList<Layer> GetLayers() => [.. _layers];

    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) =>
        [.. _layerRules.Where(r => r.AssetTypeId == assetTypeId)];

    public void LoadAll(IEnumerable<GeoFeature> features)
    {
        _features.Clear();
        foreach (var f in features) _features[f.Id] = f;
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
