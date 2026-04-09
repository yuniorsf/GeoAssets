using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Provider.InMemory;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// <see cref="IAssetProvider"/> backed by a remote GeoAssets REST API.
///
/// Reads are served from a local <see cref="InMemoryAssetProvider"/> cache populated
/// at <see cref="InitializeAsync"/> time. Writes are applied to the cache immediately
/// (so events fire synchronously) and forwarded to the server in the background.
///
/// Spatial and topology queries run against the local cache using NTS / TopoGraph,
/// so they reflect the last snapshot loaded from the server.
/// </summary>
public sealed class RestAssetProvider : IAssetProvider
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.GetOptions();

    private readonly HttpClient            _http;
    private readonly InMemoryAssetProvider _cache = new();

    public RestAssetProvider(HttpClient http) => _http = http;

    // ── Events — forwarded from the cache ─────────────────────────────────

    public event EventHandler<GeoFeature>? FeatureAdded
    {
        add    => _cache.FeatureAdded += value;
        remove => _cache.FeatureAdded -= value;
    }
    public event EventHandler<GeoFeature>? FeatureUpdated
    {
        add    => _cache.FeatureUpdated += value;
        remove => _cache.FeatureUpdated -= value;
    }
    public event EventHandler<string>? FeatureDeleted
    {
        add    => _cache.FeatureDeleted += value;
        remove => _cache.FeatureDeleted -= value;
    }
    public event EventHandler? CollectionChanged
    {
        add    => _cache.CollectionChanged += value;
        remove => _cache.CollectionChanged -= value;
    }

    // ── Initialization ─────────────────────────────────────────────────────

    /// <summary>Loads the full dataset from the remote API into the local cache.</summary>
    internal async Task InitializeAsync(CancellationToken ct = default)
    {
        var features = await _http.GetFromJsonAsync<GeoFeature[]>("features",    _opts, ct) ?? [];
        var types    = await _http.GetFromJsonAsync<AssetType[]> ("asset-types", _opts, ct) ?? [];

        _cache.LoadAll(features);
        foreach (var t in types.Where(t => !t.IsBuiltIn))
            _cache.AddAssetType(t);
    }

    // ── Reads — served from local cache ───────────────────────────────────

    public GeoFeature?                              GetById(string id)                              => _cache.GetById(id);
    public IReadOnlyList<GeoFeature>                GetAll()                                        => _cache.GetAll();
    public IReadOnlyList<GeoFeature>                GetByAssetType(string assetTypeId)             => _cache.GetByAssetType(assetTypeId);
    public IReadOnlyList<GeoFeature>                Search(string query)                            => _cache.Search(query);
    public IReadOnlyList<GeoFeature>                GetWithin(GeoGeometry bounds)                  => _cache.GetWithin(bounds);
    public IReadOnlyList<GeoFeature>                GetIntersecting(GeoGeometry geometry)          => _cache.GetIntersecting(geometry);

    /// <summary>
    /// Fetches only features within the viewport from the server,
    /// enabling the server to filter via PostGIS rather than loading the full dataset.
    /// </summary>
    public async Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var url = $"features/bounds?minLon={minLon}&minLat={minLat}&maxLon={maxLon}&maxLat={maxLat}";
        return await _http.GetFromJsonAsync<GeoFeature[]>(url, _opts) ?? [];
    }

    /// <summary>
    /// Returns the raw HTTP response body as a JSON string without any C# parsing,
    /// so JavaScript can parse it natively (avoids the WASM JSON-parsing bottleneck).
    /// </summary>
    public async Task<string?> GetInBoundsRawJsonAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var url = $"features/bounds?minLon={minLon}&minLat={minLat}&maxLon={maxLon}&maxLat={maxLat}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Returns the raw JSON elements from the server response directly — no deserialize + re-serialize round-trip.
    /// The HTTP response body (a JSON array of features) is parsed once into <see cref="JsonElement"/> objects
    /// and forwarded as-is to the map renderer.
    /// </summary>
    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var url = $"features/bounds?minLon={minLon}&minLat={minLat}&maxLon={maxLon}&maxLat={maxLat}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Expected JSON array from server");
        // Clone each element so they own their own memory and survive the document disposal.
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
        // Alternatively, we could use GetFromJsonAsync<JsonElement[]>(url) to get an array directly,
        // but that would require buffering the entire response in memory as a string first,
        // which is less efficient for large datasets.
        // return await _http.GetFromJsonAsync<JsonElement[]>(url) ?? [];
    }
    public IReadOnlyList<GeoFeature>                GetNearby(GeoPoint center, double distanceDeg) => _cache.GetNearby(center, distanceDeg);
    public IReadOnlyList<GeoFeature>                GetNeighbors(string featureId)                 => _cache.GetNeighbors(featureId);
    public IReadOnlyList<GeoFeature>                GetDescendants(string featureId)               => _cache.GetDescendants(featureId);
    public IReadOnlyList<GeoFeature>                GetAncestors(string featureId)                 => _cache.GetAncestors(featureId);
    public IReadOnlyList<GeoFeature>                FindPath(string fromId, string toId)           => _cache.FindPath(fromId, toId);
    public IReadOnlyList<GeoFeature>                FindShortestPath(string fromId, string toId)   => _cache.FindShortestPath(fromId, toId);
    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents()                       => _cache.GetConnectedComponents();
    public bool                                     HasCycles()                                     => _cache.HasCycles();
    public IReadOnlyList<GeoFeature>                TopologicalSort()                              => _cache.TopologicalSort();
    public IReadOnlyList<AssetType>                 GetAssetTypes()                                 => _cache.GetAssetTypes();

    // ── Writes — cache-first + fire-and-forget HTTP sync ──────────────────

    public void Add(GeoFeature feature)
    {
        _cache.Add(feature);
        _ = _http.PostAsJsonAsync("features", feature, _opts);
    }

    public void Update(GeoFeature feature)
    {
        _cache.Update(feature);
        _ = _http.PutAsJsonAsync($"features/{feature.Id}", feature, _opts);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        var list = features.ToList();
        _cache.AddRange(list);
        _ = _http.PostAsJsonAsync("features/bulk", list, _opts);
    }

    public void Delete(string id)
    {
        _cache.Delete(id);
        _ = _http.DeleteAsync($"features/{id}");
    }

    public void Clear()
    {
        _cache.Clear();
        _ = _http.DeleteAsync("features");
    }

    public void LoadAll(IEnumerable<GeoFeature> features)
    {
        _cache.LoadAll(features);
        _ = _http.PostAsJsonAsync("features/load", _cache.GetAll(), _opts);
    }

    public void AddAssetType(AssetType assetType)
    {
        _cache.AddAssetType(assetType);
        _ = _http.PostAsJsonAsync("asset-types", assetType, _opts);
    }

    public void DeleteAssetType(Guid id)
    {
        _cache.DeleteAssetType(id);
        _ = _http.DeleteAsync($"asset-types/{id}");
    }
}
