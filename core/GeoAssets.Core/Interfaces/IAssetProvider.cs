using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Interfaces;

public interface IAssetProvider
{
    GeoFeature? GetById(string id);
    IReadOnlyList<GeoFeature> GetAll();
    IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId);
    IReadOnlyList<GeoFeature> Search(string query);

    // ── Spatial queries (NTS-backed) ──────────────────────────────────────────

    /// <summary>Returns features whose geometry is entirely within <paramref name="bounds"/>.</summary>
    IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds);

    /// <summary>Returns features whose geometry intersects <paramref name="geometry"/>.</summary>
    IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry);

    /// <summary>
    /// Returns features whose geometry intersects the given bounding box.
    /// Implementations backed by a remote or database store should push the
    /// filter server-side; in-memory implementations filter locally.
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat);

    /// <summary>
    /// Returns the same features as <see cref="GetInBoundsAsync"/> but already serialized
    /// as <see cref="JsonElement"/> objects, avoiding a round-trip deserialize → re-serialize
    /// before the data is forwarded to the JavaScript map.
    /// Remote providers (e.g. REST) can forward the raw HTTP response JSON directly.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat);

    /// <summary>
    /// Returns features whose geometry is within <paramref name="distanceDegrees"/> of
    /// <paramref name="center"/>, ordered by ascending distance.
    /// </summary>
    IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees);

    // ── Topology queries (graph-backed) ───────────────────────────────────────

    /// <summary>Returns the direct downstream neighbors of <paramref name="featureId"/>.</summary>
    IReadOnlyList<GeoFeature> GetNeighbors(string featureId);

    /// <summary>Returns all features reachable from <paramref name="featureId"/> (BFS forward).</summary>
    IReadOnlyList<GeoFeature> GetDescendants(string featureId);

    /// <summary>Returns all features that can reach <paramref name="featureId"/> (BFS reverse).</summary>
    IReadOnlyList<GeoFeature> GetAncestors(string featureId);

    /// <summary>BFS shortest path (fewest hops) between two features. Empty when unreachable.</summary>
    IReadOnlyList<GeoFeature> FindPath(string fromId, string toId);

    /// <summary>
    /// Dijkstra shortest path (minimum total <see cref="TopoEdge.Weight"/>) between two features.
    /// Empty when unreachable.
    /// </summary>
    IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId);

    /// <summary>
    /// Groups all features into weakly connected components (edge direction ignored).
    /// Isolated features each form their own single-element component.
    /// </summary>
    IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents();

    /// <summary>Returns <c>true</c> when the directed topology graph contains at least one cycle.</summary>
    bool HasCycles();

    /// <summary>
    /// Returns features in topological (process) order — sources first.
    /// Throws <see cref="InvalidOperationException"/> when the graph contains cycles.
    /// </summary>
    IReadOnlyList<GeoFeature> TopologicalSort();

    void Add(GeoFeature feature);
    void Update(GeoFeature feature);

    /// <summary>
    /// Merges <paramref name="features"/> into the repository (add-or-update semantics)
    /// and fires <see cref="CollectionChanged"/> exactly once when done.
    /// </summary>
    void AddRange(IEnumerable<GeoFeature> features);

    void Delete(string id);
    void Clear();
    void LoadAll(IEnumerable<GeoFeature> features);

    // Asset type management
    IReadOnlyList<AssetType> GetAssetTypes();
    void AddAssetType(AssetType assetType);
    void DeleteAssetType(Guid id);

    event EventHandler<GeoFeature>? FeatureAdded;
    event EventHandler<GeoFeature>? FeatureUpdated;
    event EventHandler<string>? FeatureDeleted;
    event EventHandler? CollectionChanged;
}
