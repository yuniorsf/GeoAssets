using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.WFS;

/// <summary>
/// Read-only <see cref="IAssetProvider"/> backed by an OGC WFS 2.0 endpoint.
///
/// On initialisation all features are loaded from the WFS service and held in a
/// <see cref="LocalFeatureCache"/>.  Most read operations are served from
/// that cache.  <see cref="GetInBoundsAsync"/> issues a live WFS GetFeature request
/// with a BBOX filter so PostGIS on the server does the spatial cut — only the
/// features inside the current viewport travel over the wire.
///
/// Write operations are not supported (WFS-T is out of scope).  Any call to a
/// mutating method is silently ignored and a warning is logged.
/// </summary>
public sealed partial class WfsAssetProvider : IAssetProvider, ISyncProvider
{
    private readonly WfsClient              _wfs;
    private readonly string                 _typeName;
    private readonly int                    _maxFeatures;
    private readonly LocalFeatureCache      _cache = new();
    private readonly ILogger<WfsAssetProvider> _logger;

    internal WfsAssetProvider(
        WfsClient wfs,
        string    typeName,
        int       maxFeatures,
        ILogger<WfsAssetProvider> logger)
    {
        _wfs         = wfs;
        _typeName    = typeName;
        _maxFeatures = maxFeatures;
        _logger      = logger;
    }

    // ── Events — forwarded from the in-memory cache ──────────────────────────

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

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies connectivity via GetCapabilities then loads the full initial
    /// dataset into the local cache via GetFeature.
    /// </summary>
    internal async Task InitializeAsync(CancellationToken ct = default)
    {
        var caps = await _wfs.GetCapabilitiesAsync(ct).ConfigureAwait(false);
        LogConnected(_logger, caps.Title, caps.Version, caps.FeatureTypes.Count);

        var features = await _wfs.GetFeatureAsync(_typeName, _maxFeatures, ct).ConfigureAwait(false);
        _cache.LoadAll(features);
        LogInitialLoadComplete(_logger, features.Length, _typeName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "WFS connected: {Title} v{Version} — {Count} feature type(s) available")]
    private static partial void LogConnected(ILogger logger, string title, string version, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "WFS initial load complete: {Count} features from type '{TypeName}'")]
    private static partial void LogInitialLoadComplete(ILogger logger, int count, string typeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WFS provider is read-only. Write operations are ignored. Use a writable provider (InMemory or REST) for edits.")]
    private static partial void LogReadOnlyWrite(ILogger logger);

    // ── Reads — served from cache ─────────────────────────────────────────────

    public GeoFeature?                              GetById(string id)                             => _cache.GetById(id);
    public IReadOnlyList<GeoFeature>                GetAll()                                       => _cache.GetAll();
    public IReadOnlyList<GeoFeature>                GetByAssetType(string assetTypeId)            => _cache.GetByAssetType(assetTypeId);
    public IReadOnlyList<GeoFeature>                Search(string query)                           => _cache.Search(query);
    public IReadOnlyList<GeoFeature>                GetWithin(GeoGeometry bounds)                 => _cache.GetWithin(bounds);
    public IReadOnlyList<GeoFeature>                GetIntersecting(GeoGeometry geometry)         => _cache.GetIntersecting(geometry);
    public IReadOnlyList<GeoFeature>                GetNearby(GeoPoint center, double distanceDegrees)=> _cache.GetNearby(center, distanceDegrees);
    public IReadOnlyList<AssetType>                 GetAssetTypes()                                => _cache.GetAssetTypes();
    public IReadOnlyList<Layer>                     GetLayers()                                    => _cache.GetLayers();
    public IReadOnlyList<LayerRule>                 GetLayerRules(Guid assetTypeId)                => _cache.GetLayerRules(assetTypeId);

    // ── Spatial viewport query — live WFS request ────────────────────────────

    /// <summary>
    /// Fetches only features within the map viewport by sending a live WFS
    /// GetFeature request with a BBOX filter, letting PostGIS do the spatial cut.
    /// Results are not cached (they change with every viewport change).
    /// </summary>
    public async Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        return await _wfs.GetFeatureInBoundsAsync(
            _typeName, minLon, minLat, maxLon, maxLat, _maxFeatures).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var features = await GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
        // Re-serialize to JsonElement[] so the map renderer can forward raw JSON to JS.
        return [.. features.Select(f =>
            JsonSerializer.SerializeToElement(f, GeoAssets.Core.Services.GeoJsonSerializer.Options))];
    }

    public async Task<string?> GetInBoundsRawJsonAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var features = await GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
        return JsonSerializer.Serialize(features, GeoAssets.Core.Services.GeoJsonSerializer.Options);
    }

    // ── Writes — not supported (read-only WFS provider) ─────────────────────

    public void Add(GeoFeature feature)           => LogReadOnly();
    public void Update(GeoFeature feature)        => LogReadOnly();
    public void Delete(string id)                 => LogReadOnly();
    public void AddRange(IEnumerable<GeoFeature> features) => LogReadOnly();
    public void Clear()                           => LogReadOnly();
    public void LoadAll(IEnumerable<GeoFeature> features)  => LogReadOnly();
    public void AddAssetType(AssetType assetType) => LogReadOnly();
    public void DeleteAssetType(Guid id)          => LogReadOnly();
    public void AddLayer(Layer layer)             => LogReadOnly();
    public void DeleteLayer(Guid id)              => LogReadOnly();
    public void AddLayerRule(LayerRule layerRule) => LogReadOnly();
    public void DeleteLayerRule(Guid id)          => LogReadOnly();

    private void LogReadOnly() => LogReadOnlyWrite(_logger);
}
