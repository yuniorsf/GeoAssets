using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Provider.WMS;

/// <summary>
/// Read-only, empty <see cref="IAssetProvider"/> that also implements <see cref="IWmsProvider"/>.
///
/// Features are rendered server-side as raster PNG tiles by the WMS endpoint — there is
/// nothing to load into a local feature cache.  All feature-query methods return empty
/// collections.  Write methods are silently ignored.
///
/// The pool panel detects <see cref="IWmsProvider"/> and calls
/// <c>IMapInterop.AddWmsLayerAsync</c> rather than iterating <c>GetAll()</c>.
/// </summary>
public sealed class WmsAssetProvider : IAssetProvider, IWmsProvider
{
    public string WmsBaseUrl   { get; }
    public string WmsLayerName { get; }
    public string WmsFormat    { get; }

    public WmsAssetProvider(string wmsBaseUrl, string layerName, string format = "image/png")
    {
        WmsBaseUrl   = wmsBaseUrl;
        WmsLayerName = layerName;
        WmsFormat    = format;
    }

    // ── Events — never fire (no features are held locally) ───────────────────
#pragma warning disable CS0067
    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>?     FeatureDeleted;
    public event EventHandler?             CollectionChanged;
#pragma warning restore CS0067

    // ── Feature reads — always empty ─────────────────────────────────────────
    public GeoFeature?                              GetById(string id)                              => null;
    public IReadOnlyList<GeoFeature>                GetAll()                                        => [];
    public IReadOnlyList<GeoFeature>                GetByAssetType(string assetTypeId)             => [];
    public IReadOnlyList<GeoFeature>                Search(string query)                            => [];
    public IReadOnlyList<GeoFeature>                GetWithin(GeoGeometry bounds)                  => [];
    public IReadOnlyList<GeoFeature>                GetIntersecting(GeoGeometry geometry)          => [];
    public IReadOnlyList<GeoFeature>                GetNearby(GeoPoint center, double distanceDeg) => [];
    public Task<IReadOnlyList<GeoFeature>>          GetInBoundsAsync(double a, double b, double c, double d)
        => Task.FromResult<IReadOnlyList<GeoFeature>>([]);
    public Task<IReadOnlyList<JsonElement>>         GetInBoundsJsonAsync(double a, double b, double c, double d)
        => Task.FromResult<IReadOnlyList<JsonElement>>([]);
    public Task<string?>                            GetInBoundsRawJsonAsync(double a, double b, double c, double d)
        => Task.FromResult<string?>(null);

    // ── Topology — always empty ───────────────────────────────────────────────
    public IReadOnlyList<GeoFeature>                GetNeighbors(string featureId)                 => [];
    public IReadOnlyList<GeoFeature>                GetDescendants(string featureId)               => [];
    public IReadOnlyList<GeoFeature>                GetAncestors(string featureId)                 => [];
    public IReadOnlyList<GeoFeature>                FindPath(string fromId, string toId)           => [];
    public IReadOnlyList<GeoFeature>                FindShortestPath(string fromId, string toId)   => [];
    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents()                       => [];
    public bool                                     HasCycles()                                     => false;
    public IReadOnlyList<GeoFeature>                TopologicalSort()                              => [];

    // ── Asset types — empty (types are managed by the WMS server) ────────────
    public IReadOnlyList<AssetType> GetAssetTypes()           => [.. AssetType.Defaults];
    public void AddAssetType(AssetType assetType)             { }
    public void DeleteAssetType(Guid id)                      { }

    // ── Writes — silently ignored (read-only WMS provider) ───────────────────
    public void Add(GeoFeature feature)                       { }
    public void Update(GeoFeature feature)                    { }
    public void Delete(string id)                             { }
    public void AddRange(IEnumerable<GeoFeature> features)    { }
    public void Clear()                                       { }
    public void LoadAll(IEnumerable<GeoFeature> features)     { }
}
