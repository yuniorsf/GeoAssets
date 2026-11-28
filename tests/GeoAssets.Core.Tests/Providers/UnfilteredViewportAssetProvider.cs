using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Core.Tests.Providers;

/// <summary>
/// A provider whose own <see cref="GetInViewportAsync"/> pushdown answers <em>every</em> feature, unfiltered, and counts
/// its calls (XD01-430) — so a decorator that forwarded it would leak them; every other member is
/// <paramref name="inner"/>'s.
/// </summary>
internal sealed class UnfilteredViewportAssetProvider(TestAssetProvider inner) : IAssetProvider
{
    public int ViewportCalls { get; private set; }

    /// <summary>The token the last cancellable <see cref="GetPageAsync(AssetQuery, CancellationToken)"/> got (XD01-478).</summary>
    public CancellationToken? PageToken { get; private set; }

    public Task<PagedResult<GeoFeature>> GetPageAsync(AssetQuery query, CancellationToken ct)
    {
        PageToken = ct;
        return ((IAssetProvider)inner).GetPageAsync(query, CancellationToken.None);
    }

    /// <summary>The token the last cancellable <see cref="GetInBoundsAsync(double, double, double, double, CancellationToken)"/> got (XD01-481).</summary>
    public CancellationToken? BoundsToken { get; private set; }

    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat, CancellationToken ct)
    {
        BoundsToken = ct;
        return inner.GetInBoundsAsync(minLon, minLat, maxLon, maxLat);
    }

    public Task<IReadOnlyList<GeoFeature>> GetInViewportAsync(ViewportQuery viewport, int maxCount, CancellationToken ct = default)
    {
        ViewportCalls++;
        return Task.FromResult(inner.GetAll());
    }

    public event EventHandler<GeoFeature>? FeatureAdded { add => inner.FeatureAdded += value; remove => inner.FeatureAdded -= value; }
    public event EventHandler<GeoFeature>? FeatureUpdated { add => inner.FeatureUpdated += value; remove => inner.FeatureUpdated -= value; }
    public event EventHandler<string>? FeatureDeleted { add => inner.FeatureDeleted += value; remove => inner.FeatureDeleted -= value; }
    public event EventHandler? CollectionChanged { add => inner.CollectionChanged += value; remove => inner.CollectionChanged -= value; }

    public GeoFeature? GetById(string id) => inner.GetById(id);
    public IReadOnlyList<GeoFeature> GetAll() => inner.GetAll();
    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => inner.GetByAssetType(assetTypeId);
    public IReadOnlyList<GeoFeature> Search(string query) => inner.Search(query);
    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => inner.GetWithin(bounds);
    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => inner.GetIntersecting(geometry);
    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => inner.GetInBoundsAsync(minLon, minLat, maxLon, maxLat);
    public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => inner.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);
    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => inner.GetNearby(center, distanceDegrees);
    public void Add(GeoFeature feature) => inner.Add(feature);
    public void Update(GeoFeature feature) => inner.Update(feature);
    public void AddRange(IEnumerable<GeoFeature> features) => inner.AddRange(features);
    public void Delete(string id) => inner.Delete(id);
    public void Clear() => inner.Clear();
    public void LoadAll(IEnumerable<GeoFeature> features) => inner.LoadAll(features);
    public IReadOnlyList<AssetType> GetAssetTypes() => inner.GetAssetTypes();
    public void AddAssetType(AssetType assetType) => inner.AddAssetType(assetType);
    public void DeleteAssetType(Guid id) => inner.DeleteAssetType(id);
    public IReadOnlyList<Layer> GetLayers() => inner.GetLayers();
    public void AddLayer(Layer layer) => inner.AddLayer(layer);
    public void DeleteLayer(Guid id) => inner.DeleteLayer(id);
    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => inner.GetLayerRules(assetTypeId);
    public void AddLayerRule(LayerRule layerRule) => inner.AddLayerRule(layerRule);
    public void DeleteLayerRule(Guid id) => inner.DeleteLayerRule(id);
}
