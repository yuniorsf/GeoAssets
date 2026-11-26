using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// A provider holding <paramref name="features"/> for the layer renderer tests (XD01-422): only the
/// reads a renderer makes work — <see cref="GetAll"/>, the bounds JSON reads and the asset types —
/// every other member throws. <see cref="RawJson"/> is what <see cref="GetInBoundsRawJsonAsync"/>
/// answers (<c>null</c> = no raw-JSON support, as Postgres); <see cref="Json"/> what
/// <see cref="GetInBoundsJsonAsync"/> answers. The bounds last asked for are kept.
/// </summary>
internal class FakeLayerProvider(params GeoFeature[] features) : IAssetProvider
{
    public string? RawJson { get; init; }
    public IReadOnlyList<JsonElement> Json { get; init; } = [];
    public (double MinLon, double MinLat, double MaxLon, double MaxLat)? LastBounds { get; private set; }

    public IReadOnlyList<GeoFeature> GetAll() => features;

    public Task<string?> GetInBoundsRawJsonAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        LastBounds = (minLon, minLat, maxLon, maxLat);
        return Task.FromResult(RawJson);
    }

    /// <summary>Runs while <see cref="GetInBoundsJsonAsync"/> is in flight (e.g. a newer pan cancelling this one).</summary>
    public Func<Task>? WhileFetchingJson { get; init; }

    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        LastBounds = (minLon, minLat, maxLon, maxLat);
        if (WhileFetchingJson is { } inFlight) await inFlight();
        return Json;
    }

    public IReadOnlyList<AssetType> GetAssetTypes() => [.. AssetType.Defaults];

    public GeoFeature? GetById(string id) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> Search(string query) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => throw new NotSupportedException();
    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => throw new NotSupportedException();
    public void Add(GeoFeature feature) => throw new NotSupportedException();
    public void Update(GeoFeature feature) => throw new NotSupportedException();
    public void AddRange(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
    public void Delete(string id) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void LoadAll(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
    public void AddAssetType(AssetType assetType) => throw new NotSupportedException();
    public void DeleteAssetType(Guid id) => throw new NotSupportedException();
    public IReadOnlyList<Layer> GetLayers() => throw new NotSupportedException();
    public void AddLayer(Layer layer) => throw new NotSupportedException();
    public void DeleteLayer(Guid id) => throw new NotSupportedException();
    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => throw new NotSupportedException();
    public void AddLayerRule(LayerRule layerRule) => throw new NotSupportedException();
    public void DeleteLayerRule(Guid id) => throw new NotSupportedException();

    // Never fire — no test relies on these.
    public event EventHandler<GeoFeature>? FeatureAdded { add { } remove { } }
    public event EventHandler<GeoFeature>? FeatureUpdated { add { } remove { } }
    public event EventHandler<string>? FeatureDeleted { add { } remove { } }
    public event EventHandler? CollectionChanged { add { } remove { } }
}
