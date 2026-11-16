using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Tests.Providers;

/// <summary>
/// An <see cref="IAssetProvider"/> on which only <see cref="GetAll"/> works (and is counted) —
/// every other member throws, proving a caller never falls back to the provider's own
/// synchronous spatial or lookup members.
/// </summary>
internal sealed class GetAllOnlyAssetProvider(Action? onGetAll = null, params GeoFeature[] features) : IAssetProvider
{
    public int GetAllCalls { get; private set; }

    public event EventHandler<GeoFeature>? FeatureAdded { add => throw Unexpected(); remove => throw Unexpected(); }
    public event EventHandler<GeoFeature>? FeatureUpdated { add => throw Unexpected(); remove => throw Unexpected(); }
    public event EventHandler<string>? FeatureDeleted { add => throw Unexpected(); remove => throw Unexpected(); }
    public event EventHandler? CollectionChanged { add => throw Unexpected(); remove => throw Unexpected(); }

    public IReadOnlyList<GeoFeature> GetAll()
    {
        GetAllCalls++;
        onGetAll?.Invoke();
        return features;
    }

    public GeoFeature? GetById(string id) => throw Unexpected();
    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => throw Unexpected();
    public IReadOnlyList<GeoFeature> Search(string query) => throw Unexpected();
    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => throw Unexpected();
    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => throw Unexpected();
    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => throw Unexpected();
    public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => throw Unexpected();
    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => throw Unexpected();
    public void Add(GeoFeature feature) => throw Unexpected();
    public void Update(GeoFeature feature) => throw Unexpected();
    public void AddRange(IEnumerable<GeoFeature> features) => throw Unexpected();
    public void Delete(string id) => throw Unexpected();
    public void Clear() => throw Unexpected();
    public void LoadAll(IEnumerable<GeoFeature> features) => throw Unexpected();
    public IReadOnlyList<AssetType> GetAssetTypes() => throw Unexpected();
    public void AddAssetType(AssetType assetType) => throw Unexpected();
    public void DeleteAssetType(Guid id) => throw Unexpected();
    public IReadOnlyList<Layer> GetLayers() => throw Unexpected();
    public void AddLayer(Layer layer) => throw Unexpected();
    public void DeleteLayer(Guid id) => throw Unexpected();
    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => throw Unexpected();
    public void AddLayerRule(LayerRule layerRule) => throw Unexpected();
    public void DeleteLayerRule(Guid id) => throw Unexpected();

    private static InvalidOperationException Unexpected() =>
        new("Only IAssetProvider.GetAll may be called by the code under test.");
}
