using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

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

    // Never fire — WfsAssetProvider is read-only (WFS-T is out of scope), so no code path ever
    // adds/updates/deletes a feature. Declared (and forwarded by WfsAssetProvider) solely to
    // satisfy the IAssetProvider contract, matching WmsAssetProvider's identical situation.
#pragma warning disable CS0067
    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
#pragma warning restore CS0067
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

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Within(bounds))];

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) =>
        [.. _features.Values.Where(f => f.Geometry is not null && f.Geometry.Intersects(geometry))];

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        [.. _features.Values
            .Where(f => f.Geometry is not null && f.Geometry.Distance(center) <= distanceDegrees)
            .OrderBy(f => f.Geometry!.Distance(center))];

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
