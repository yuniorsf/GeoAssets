using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Providers;

/// <summary>
/// <see cref="IAssetProvider"/> proxy that transparently delegates to whichever
/// entry is currently <see cref="IProviderPool.Active"/>.
///
/// When the active entry changes, the proxy re-wires its internal subscriptions
/// and fires <see cref="CollectionChanged"/> so all UI consumers (AssetList, AssetForm, etc.)
/// refresh without knowing a switch occurred.
/// </summary>
public sealed class ActiveAssetProvider : IAssetProvider
{
    private readonly IProviderPool _pool;
    private IAssetProvider _current;

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>?     FeatureDeleted;
    public event EventHandler?             CollectionChanged;

    public ActiveAssetProvider(IProviderPool pool)
    {
        _pool    = pool;
        _current = NullAssetProvider.Instance;
        pool.Changed += OnPoolChanged;
    }

    private void OnPoolChanged(object? _, EventArgs __)
    {
        if (_pool.All.Count == 0 || !_pool.All.Any(e => e.IsActive)) return;
        var next = _pool.Active.Provider;
        if (ReferenceEquals(next, _current)) return;
        Unsubscribe(_current);
        _current = next;
        Subscribe(_current);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Subscribe(IAssetProvider repo)
    {
        repo.FeatureAdded      += OnFeatureAdded;
        repo.FeatureUpdated    += OnFeatureUpdated;
        repo.FeatureDeleted    += OnFeatureDeleted;
        repo.CollectionChanged += OnCollectionChanged;
    }

    private void Unsubscribe(IAssetProvider repo)
    {
        repo.FeatureAdded      -= OnFeatureAdded;
        repo.FeatureUpdated    -= OnFeatureUpdated;
        repo.FeatureDeleted    -= OnFeatureDeleted;
        repo.CollectionChanged -= OnCollectionChanged;
    }

    private void OnFeatureAdded(object? s, GeoFeature f)    => FeatureAdded?.Invoke(s, f);
    private void OnFeatureUpdated(object? s, GeoFeature f)  => FeatureUpdated?.Invoke(s, f);
    private void OnFeatureDeleted(object? s, string id)     => FeatureDeleted?.Invoke(s, id);
    private void OnCollectionChanged(object? s, EventArgs e) => CollectionChanged?.Invoke(s, e);

    // ── All members delegate to _current ────────────────────────────────────

    public GeoFeature?                              GetById(string id)                                => _current.GetById(id);
    public IReadOnlyList<GeoFeature>                GetAll()                                          => _current.GetAll();
    public IReadOnlyList<GeoFeature>                GetByAssetType(string assetTypeId)               => _current.GetByAssetType(assetTypeId);
    public IReadOnlyList<GeoFeature>                Search(string query)                              => _current.Search(query);
    public IReadOnlyList<GeoFeature>                GetWithin(GeoGeometry bounds)                    => _current.GetWithin(bounds);
    public IReadOnlyList<GeoFeature>                GetIntersecting(GeoGeometry geometry)            => _current.GetIntersecting(geometry);
    public Task<IReadOnlyList<GeoFeature>>          GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat)     => _current.GetInBoundsAsync(minLon, minLat, maxLon, maxLat);
    public Task<IReadOnlyList<JsonElement>>         GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => _current.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);
    public IReadOnlyList<GeoFeature>                GetNearby(GeoPoint center, double distanceDeg)   => _current.GetNearby(center, distanceDeg);
    public IReadOnlyList<GeoFeature>                GetNeighbors(string featureId)                   => _current.GetNeighbors(featureId);
    public IReadOnlyList<GeoFeature>                GetDescendants(string featureId)                 => _current.GetDescendants(featureId);
    public IReadOnlyList<GeoFeature>                GetAncestors(string featureId)                   => _current.GetAncestors(featureId);
    public IReadOnlyList<GeoFeature>                FindPath(string fromId, string toId)             => _current.FindPath(fromId, toId);
    public IReadOnlyList<GeoFeature>                FindShortestPath(string fromId, string toId)     => _current.FindShortestPath(fromId, toId);
    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents()                         => _current.GetConnectedComponents();
    public bool                                     HasCycles()                                       => _current.HasCycles();
    public IReadOnlyList<GeoFeature>                TopologicalSort()                                 => _current.TopologicalSort();
    public IReadOnlyList<AssetType>                 GetAssetTypes()                                   => _current.GetAssetTypes();

    public void Add(GeoFeature feature)                    => _current.Add(feature);
    public void Update(GeoFeature feature)                 => _current.Update(feature);
    public void AddRange(IEnumerable<GeoFeature> features) => _current.AddRange(features);
    public void Delete(string id)                          => _current.Delete(id);
    public void Clear()                                    => _current.Clear();
    public void LoadAll(IEnumerable<GeoFeature> features)  => _current.LoadAll(features);
    public void AddAssetType(AssetType assetType)          => _current.AddAssetType(assetType);
    public void DeleteAssetType(Guid id)                   => _current.DeleteAssetType(id);
}
