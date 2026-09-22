using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// <see cref="IAssetProvider"/> backed by a remote GeoAssets REST API.
///
/// Reads are served from a local <see cref="LocalFeatureCache"/> populated at
/// <see cref="InitializeAsync"/> time. Writes are applied to the cache immediately
/// (so events fire synchronously) and forwarded to the server in the background.
///
/// Spatial and topology queries run against the local cache using NTS / TopoGraph,
/// so they reflect the last snapshot loaded from the server.
/// </summary>
public sealed class RestAssetProvider : IAssetProvider
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.GetOptions();

    /// <summary>Bboxes at or below this area (in decimal-degrees²) are fetched as a single
    /// request — tiling a small/already-fast viewport would only add HTTP overhead.</summary>
    private const double TileAreaThresholdDegSq = 50.0;
    private const int    TileGridDivisions      = 2; // 2x2 = 4 tiles once the threshold is exceeded
    private const int    MaxConcurrentBoundsFetches = 4;

    private readonly HttpClient        _http;
    private readonly LocalFeatureCache _cache = new();

    /// <summary>Bounds total in-flight <c>features/bounds</c> requests across overlapping calls
    /// (not just within one tiled fetch) — rapid panning without this would let each viewport
    /// change's tile batch pile on top of the last, worsening the contention XD01-170 flags.</summary>
    private readonly SemaphoreSlim _boundsFetchThrottle = new(MaxConcurrentBoundsFetches, MaxConcurrentBoundsFetches);

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
    /// Splits a bounding box into a <paramref name="gridDivisions"/> x <paramref name="gridDivisions"/>
    /// grid of sub-tiles by midpoint bisection. Pure and side-effect-free so it's directly
    /// unit-testable without an <see cref="HttpClient"/>.
    /// </summary>
    public static IReadOnlyList<(double MinLon, double MinLat, double MaxLon, double MaxLat)> SplitIntoTiles(
        double minLon, double minLat, double maxLon, double maxLat, int gridDivisions = TileGridDivisions)
    {
        var lonStep = (maxLon - minLon) / gridDivisions;
        var latStep = (maxLat - minLat) / gridDivisions;
        var tiles   = new List<(double, double, double, double)>(gridDivisions * gridDivisions);

        for (var row = 0; row < gridDivisions; row++)
            for (var col = 0; col < gridDivisions; col++)
            {
                var tileMinLon = minLon + col * lonStep;
                var tileMinLat = minLat + row * latStep;
                tiles.Add((tileMinLon, tileMinLat, tileMinLon + lonStep, tileMinLat + latStep));
            }

        return tiles;
    }

    /// <summary>
    /// Streams raw JSON chunks for the bbox, tiling into concurrent HTTP calls to
    /// <c>features/bounds</c> when the bbox area exceeds <see cref="TileAreaThresholdDegSq"/>.
    /// Chunks are yielded in completion order — whichever tile's HTTP response lands first
    /// renders first — not tile order. A feature whose geometry straddles a tile seam may be
    /// yielded twice (once per intersecting tile); <c>geoassets.js</c>'s <c>renderFeature</c> keys
    /// its Leaflet layer by feature id, so a repeat render just replaces the same layer.
    /// </summary>
    public async IAsyncEnumerable<string> GetInBoundsRawJsonChunksAsync(
        double minLon, double minLat, double maxLon, double maxLat,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var area = (maxLon - minLon) * (maxLat - minLat);
        var gridDivisions = area > TileAreaThresholdDegSq ? TileGridDivisions : 1;
        var tiles = SplitIntoTiles(minLon, minLat, maxLon, maxLat, gridDivisions);
        var tasks = tiles.Select(t => FetchTileThrottledAsync(t.MinLon, t.MinLat, t.MaxLon, t.MaxLat, ct)).ToList();

        await foreach (var completed in Task.WhenEach(tasks).WithCancellation(ct))
        {
            var raw = await completed;
            if (raw is not null) yield return raw;
        }
    }

    private async Task<string?> FetchTileThrottledAsync(
        double minLon, double minLat, double maxLon, double maxLat, CancellationToken ct)
    {
        await _boundsFetchThrottle.WaitAsync(ct);
        try
        {
            return await GetInBoundsRawJsonAsync(minLon, minLat, maxLon, maxLat);
        }
        finally
        {
            _boundsFetchThrottle.Release();
        }
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
    public IReadOnlyList<AssetType>                 GetAssetTypes()                                 => _cache.GetAssetTypes();
    public IReadOnlyList<Layer>                     GetLayers()                                     => _cache.GetLayers();
    public IReadOnlyList<LayerRule>                 GetLayerRules(Guid assetTypeId)                 => _cache.GetLayerRules(assetTypeId);

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

    /// <summary>
    /// Cache-only — no server endpoint for layers exists yet, so unlike the other write methods
    /// this doesn't forward over HTTP. Revisit once the server gains a <c>layers</c> route.
    /// </summary>
    public void AddLayer(Layer layer)             => _cache.AddLayer(layer);
    public void DeleteLayer(Guid id)              => _cache.DeleteLayer(id);
    public void AddLayerRule(LayerRule layerRule) => _cache.AddLayerRule(layerRule);
    public void DeleteLayerRule(Guid id)          => _cache.DeleteLayerRule(id);
}
