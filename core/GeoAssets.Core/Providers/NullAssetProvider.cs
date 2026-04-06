using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Providers;

/// <summary>
/// No-op <see cref="IAssetProvider"/> used as a placeholder before the first
/// real provider is connected via the boot flow. All reads return empty results;
/// all writes are silently ignored.
/// </summary>
internal sealed class NullAssetProvider : IAssetProvider
{
    public static readonly NullAssetProvider Instance = new();

    public event EventHandler<GeoFeature>? FeatureAdded      { add { } remove { } }
    public event EventHandler<GeoFeature>? FeatureUpdated    { add { } remove { } }
    public event EventHandler<string>?     FeatureDeleted    { add { } remove { } }
    public event EventHandler?             CollectionChanged { add { } remove { } }

    public GeoFeature?                              GetById(string id)                              => null;
    public IReadOnlyList<GeoFeature>                GetAll()                                        => [];
    public IReadOnlyList<GeoFeature>                GetByAssetType(string assetTypeId)             => [];
    public IReadOnlyList<GeoFeature>                Search(string query)                            => [];
    public IReadOnlyList<GeoFeature>                GetWithin(GeoGeometry bounds)                  => [];
    public IReadOnlyList<GeoFeature>                GetIntersecting(GeoGeometry geometry)          => [];
    public Task<IReadOnlyList<GeoFeature>>          GetInBoundsAsync(double a, double b, double c, double d)     => Task.FromResult<IReadOnlyList<GeoFeature>>([]);
    public Task<IReadOnlyList<JsonElement>>         GetInBoundsJsonAsync(double a, double b, double c, double d) => Task.FromResult<IReadOnlyList<JsonElement>>([]);
    public IReadOnlyList<GeoFeature>                GetNearby(GeoPoint center, double distanceDeg) => [];
    public IReadOnlyList<GeoFeature>                GetNeighbors(string featureId)                 => [];
    public IReadOnlyList<GeoFeature>                GetDescendants(string featureId)               => [];
    public IReadOnlyList<GeoFeature>                GetAncestors(string featureId)                 => [];
    public IReadOnlyList<GeoFeature>                FindPath(string fromId, string toId)           => [];
    public IReadOnlyList<GeoFeature>                FindShortestPath(string fromId, string toId)   => [];
    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents()                       => [];
    public bool                                     HasCycles()                                     => false;
    public IReadOnlyList<GeoFeature>                TopologicalSort()                               => [];
    public IReadOnlyList<AssetType>                 GetAssetTypes()                                 => [];

    public void Add(GeoFeature feature)                    { }
    public void Update(GeoFeature feature)                 { }
    public void AddRange(IEnumerable<GeoFeature> features) { }
    public void Delete(string id)                          { }
    public void Clear()                                    { }
    public void LoadAll(IEnumerable<GeoFeature> features)  { }
    public void AddAssetType(AssetType assetType)          { }
    public void DeleteAssetType(Guid id)                   { }
}
