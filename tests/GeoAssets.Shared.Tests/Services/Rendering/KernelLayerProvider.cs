using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// A provider of a GeoAssets server for <see cref="Shared.Services.Rendering.HybridLodMapLayerRenderer"/>'s tests
/// (XD01-436): declares <paramref name="capabilities"/> and its server's <paramref name="baseAddress"/>, holds features
/// it reads by id and writes (raising the events), and counts what would enumerate or fetch the catalog —
/// <see cref="GetAllCalls"/> (the vector renderer's whole-layer stream) and <see cref="BoundsReads"/> (its viewport
/// fetch, answered with <see cref="RawJson"/>). Every other member throws.
/// </summary>
internal sealed class KernelLayerProvider(
    ProviderRenderCapabilities capabilities = KernelLayerProvider.Kernel,
    string? baseAddress = "https://gw.example/api/geoassets/") : IAssetProvider, IProviderCapabilities, IRemoteAssetProvider
{
    /// <summary>What <c>RestAssetProvider</c> declares.</summary>
    public const ProviderRenderCapabilities Kernel =
        Core.Providers.AssetProviderExtensions.DefaultVectorCapabilities
        | ProviderRenderCapabilities.VectorTiles
        | ProviderRenderCapabilities.HybridLod;

    private readonly Dictionary<string, GeoFeature> _features = new(StringComparer.Ordinal);

    public ProviderRenderCapabilities RenderCapabilities => capabilities;
    public Uri? BaseAddress { get; } = baseAddress is null ? null : new Uri(baseAddress, UriKind.RelativeOrAbsolute);

    public string RawJson { get; init; } = "[]";
    public int GetAllCalls { get; private set; }
    public int BoundsReads { get; private set; }
    public List<string> ByIdReads { get; } = [];

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged { add { } remove { } }

    public bool HasWriteSubscribers => FeatureAdded is not null || FeatureUpdated is not null || FeatureDeleted is not null;

    /// <summary>Puts features in without raising anything (the catalog as loaded).</summary>
    public KernelLayerProvider With(params GeoFeature[] features)
    {
        foreach (var f in features) _features[f.Id] = f;
        return this;
    }

    public GeoFeature? GetById(string id)
    {
        ByIdReads.Add(id);
        return _features.GetValueOrDefault(id);
    }

    public IReadOnlyList<GeoFeature> GetAll()
    {
        GetAllCalls++;
        return [.. _features.Values];
    }

    public Task<string?> GetInBoundsRawJsonAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        BoundsReads++;
        return Task.FromResult<string?>(RawJson);
    }

    public void Add(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        FeatureAdded?.Invoke(this, feature);
    }

    public void Update(GeoFeature feature)
    {
        _features[feature.Id] = feature;
        FeatureUpdated?.Invoke(this, feature);
    }

    public void Delete(string id)
    {
        if (_features.Remove(id)) FeatureDeleted?.Invoke(this, id);
    }

    public IReadOnlyList<AssetType> GetAssetTypes() => [.. AssetType.Defaults];
    public IReadOnlyList<Layer> GetLayers() => [];

    public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> Search(string query) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => throw new NotSupportedException();
    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => throw new NotSupportedException();
    public void AddRange(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void LoadAll(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
    public void AddAssetType(AssetType assetType) => throw new NotSupportedException();
    public void DeleteAssetType(Guid id) => throw new NotSupportedException();
    public void AddLayer(Layer layer) => throw new NotSupportedException();
    public void DeleteLayer(Guid id) => throw new NotSupportedException();
    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => throw new NotSupportedException();
    public void AddLayerRule(LayerRule layerRule) => throw new NotSupportedException();
    public void DeleteLayerRule(Guid id) => throw new NotSupportedException();
}
