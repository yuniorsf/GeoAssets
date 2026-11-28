using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Interfaces;

public interface IAssetProvider
{
    GeoFeature? GetById(string id);
    IReadOnlyList<GeoFeature> GetAll();
    IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId);
    IReadOnlyList<GeoFeature> Search(string query);

    /// <summary>
    /// Returns a filtered, sorted, paged slice of assets plus the true total matching count.
    /// Default implementation falls back to <see cref="GetAll"/>/<see cref="Search"/> plus
    /// in-memory LINQ <c>Skip</c>/<c>Take</c> — providers backed by a queryable store (e.g.
    /// PostgreSQL) should override this with a server-side query that never materializes the
    /// full collection. See <see cref="AssetQuery.SortBy"/> for the enumerated sort values and
    /// the <c>Id</c> fallback ordering — every override must implement the same values and
    /// fallback as this default implementation, to keep <see cref="AssetQuery.SortBy"/>'s
    /// contract identical regardless of which provider is active.
    /// </summary>
    Task<PagedResult<GeoFeature>> GetPageAsync(AssetQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<GeoFeature> filtered = string.IsNullOrWhiteSpace(query.SearchText)
            ? GetAll()
            : Search(query.SearchText);

        if (!string.IsNullOrWhiteSpace(query.AssetTypeId))
            filtered = filtered.Where(f => string.Equals(f.Properties.AssetTypeId, query.AssetTypeId, StringComparison.Ordinal));

        if (query.OrganizationId is { } orgId)
            filtered = filtered.Where(f => f.Properties.OrganizationId == orgId);

        if (query.FeatureIds is { Count: > 0 } featureIds)
        {
            var idSet = featureIds.ToHashSet(StringComparer.Ordinal);
            filtered = filtered.Where(f => idSet.Contains(f.Id));
        }

        if (query.AttributeFilters is { Count: > 0 } attrFilters)
        {
            filtered = filtered.Where(f => attrFilters.All(kv =>
                f.Properties.CustomAttributes.TryGetValue(kv.Key, out var val) &&
                string.Equals(val?.ToString(), kv.Value, StringComparison.Ordinal)));
        }

        if (query.Bounds is { } bounds)
        {
            var box = new GeoPolygon(
            [
                (bounds.MinLon, bounds.MinLat), (bounds.MaxLon, bounds.MinLat), (bounds.MaxLon, bounds.MaxLat),
                (bounds.MinLon, bounds.MaxLat), (bounds.MinLon, bounds.MinLat),
            ]);
            filtered = filtered.Where(f => f.Geometry is not null && f.Geometry.Intersects(box));
        }

        filtered = query.SortBy switch
        {
            "name" => query.SortDescending ? filtered.OrderByDescending(f => f.Properties.Name, StringComparer.Ordinal) : filtered.OrderBy(f => f.Properties.Name, StringComparer.Ordinal),
            "createdAt" => query.SortDescending ? filtered.OrderByDescending(f => f.Properties.CreatedAt) : filtered.OrderBy(f => f.Properties.CreatedAt),
            "updatedAt" => query.SortDescending ? filtered.OrderByDescending(f => f.Properties.UpdatedAt) : filtered.OrderBy(f => f.Properties.UpdatedAt),
            _ => query.SortDescending ? filtered.OrderByDescending(f => f.Id, StringComparer.Ordinal) : filtered.OrderBy(f => f.Id, StringComparer.Ordinal)
        };

        var matching = filtered.ToList();
        var page = matching.Skip(query.Skip).Take(query.Take).ToList();
        var totalCount = query.CountLimit is { } limit
            ? Math.Min(matching.Count, limit >= int.MaxValue - 1 ? int.MaxValue : Math.Max(0, limit) + 1)
            : matching.Count;
        return Task.FromResult(new PagedResult<GeoFeature> { Items = page, TotalCount = totalCount });
    }

    /// <summary>
    /// <see cref="GetPageAsync(AssetQuery)"/>, cancellable (XD01-478): a provider backed by a queryable store overrides it
    /// to hand <paramref name="ct"/> to its query, so a caller that goes away (a WFS client disconnecting) cancels the
    /// database command; a decorator that forwards its reads forwards this too. The default can only refuse to start once
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    Task<PagedResult<GeoFeature>> GetPageAsync(AssetQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return GetPageAsync(query);
    }

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
    /// <see cref="GetInBoundsAsync(double, double, double, double)"/>, cancellable (XD01-481): a provider backed by a
    /// queryable store overrides it to hand <paramref name="ct"/> to its query, and a decorator that forwards its reads
    /// forwards this too. The default can only refuse to start once <paramref name="ct"/> is cancelled. Like the other
    /// enumerating reads, a catalog too large to hold whole may refuse a box over more features than it enumerates
    /// (<see cref="CatalogEnumerationLimitException"/>).
    /// </summary>
    Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return GetInBoundsAsync(minLon, minLat, maxLon, maxLat);
    }

    /// <summary>
    /// Returns the same features as <see cref="GetInBoundsAsync"/> but already serialized
    /// as <see cref="JsonElement"/> objects, avoiding a round-trip deserialize → re-serialize
    /// before the data is forwarded to the JavaScript map.
    /// Remote providers (e.g. REST) can forward the raw HTTP response JSON directly.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat);

    /// <summary>
    /// Returns the raw JSON array string for features within the bounding box,
    /// bypassing all C# JSON parsing so JavaScript can parse natively (much faster in WASM).
    /// Returns <c>null</c> when the provider does not support this path
    /// (callers must fall back to <see cref="GetInBoundsJsonAsync"/>).
    /// </summary>
    Task<string?> GetInBoundsRawJsonAsync(double minLon, double minLat, double maxLon, double maxLat)
        => Task.FromResult<string?>(null);

    /// <summary>
    /// Streams the raw JSON array string(s) for features within the bounding box, allowing a caller
    /// to render progressively as each chunk arrives rather than waiting for one large result.
    /// Default implementation yields the single result of <see cref="GetInBoundsRawJsonAsync"/> as
    /// one chunk (or nothing when that returns <c>null</c>) — every existing provider works
    /// unchanged. Providers that can fetch sub-regions in parallel (e.g. a REST-backed provider
    /// tiling the bbox into concurrent HTTP calls) should override this to yield each sub-region's
    /// raw JSON as it completes.
    /// </summary>
    async IAsyncEnumerable<string> GetInBoundsRawJsonChunksAsync(
        double minLon, double minLat, double maxLon, double maxLat,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var raw = await GetInBoundsRawJsonAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
        if (raw is not null) yield return raw;
    }

    /// <summary>
    /// <see cref="GetInBoundsJsonAsync(double, double, double, double)"/> for a map viewport, cancellable (XD01-429): a
    /// superseded viewport's fetch is abandoned instead of painting a stale result. The default ignores
    /// <see cref="ViewportQuery.Zoom"/> and can only refuse to start once <paramref name="ct"/> is cancelled; a provider
    /// whose fetch can be aborted mid-flight (e.g. REST) overrides it, and a decorator that forwards its reads forwards
    /// this too.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(ViewportQuery viewport, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (minLon, minLat, maxLon, maxLat) = viewport.Bounds;
        return GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);
    }

    /// <summary>
    /// <see cref="GetInBoundsRawJsonChunksAsync(double, double, double, double, CancellationToken)"/> for a map viewport
    /// (XD01-429). The default ignores <see cref="ViewportQuery.Zoom"/>; a provider that can pass the zoom on (e.g. REST)
    /// overrides it, and a decorator that forwards its reads forwards this too.
    /// </summary>
    IAsyncEnumerable<string> GetInBoundsRawJsonChunksAsync(ViewportQuery viewport, CancellationToken ct = default)
    {
        var (minLon, minLat, maxLon, maxLat) = viewport.Bounds;
        return GetInBoundsRawJsonChunksAsync(minLon, minLat, maxLon, maxLat, ct);
    }

    /// <summary>
    /// What a map draws in a viewport (XD01-430, <c>GET /features/bounds</c>): the features of
    /// <see cref="GetInBoundsAsync"/> for <see cref="ViewportQuery.Bounds"/> whose asset type is drawn at
    /// <see cref="ViewportQuery.Zoom"/> (<see cref="AssetTypeZoomRange.TypeIdsHiddenAt"/> over <see cref="GetAssetTypes"/>;
    /// every type when the zoom is unknown, and features of an unknown type always), the first
    /// <paramref name="maxCount"/> by ordinal id — the same answer on every call; none for a <paramref name="maxCount"/>
    /// of zero or less. The default filters, sorts and caps in memory over the provider's own (scoped) reads, and can only
    /// refuse to start once <paramref name="ct"/> is cancelled; a provider backed by a queryable store overrides it to do
    /// it all in the query, and a decorator that forwards its reads forwards this too — one that narrows them (e.g. to an
    /// organization) keeps this default, so the narrowing applies.
    /// </summary>
    async Task<IReadOnlyList<GeoFeature>> GetInViewportAsync(ViewportQuery viewport, int maxCount, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (maxCount <= 0) return [];

        var (minLon, minLat, maxLon, maxLat) = viewport.Bounds;
        var features = await GetInBoundsAsync(minLon, minLat, maxLon, maxLat, ct).ConfigureAwait(false);
        var hidden = viewport.Zoom is { } zoom ? AssetTypeZoomRange.TypeIdsHiddenAt(GetAssetTypes(), zoom) : null;
        return
        [
            .. features
                .Where(f => hidden is null || !hidden.Contains(f.Properties.AssetTypeId))
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .Take(maxCount),
        ];
    }

    /// <summary>
    /// Returns features whose geometry is within <paramref name="distanceDegrees"/> of
    /// <paramref name="center"/>, ordered by ascending distance.
    /// </summary>
    IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees);

    /// <summary>
    /// True when <paramref name="point"/> is within <paramref name="toleranceMeters"/> of the
    /// feature identified by <paramref name="featureId"/> — a real-world (meters), not
    /// coordinate-degree, distance (XD01-221; see <see cref="Services.GeodeticDistanceCalculator"/>
    /// for why that distinction matters and why <see cref="GetNearby"/>'s degree-based contract
    /// isn't reused here). False when the feature doesn't exist or has no geometry.
    ///
    /// Default implementation uses <see cref="Services.GeodeticDistanceCalculator"/> against the
    /// feature's <see cref="GeoGeometry.Centroid"/> — accurate enough for a field-crew arrival
    /// tolerance measured in tens of meters, and correct for every existing implementer (in-memory
    /// caches, REST, WFS) with no extra code. A provider backed by a real spatial database (e.g.
    /// <c>PostgresAssetProvider</c>) may override this with a genuine server-side geodesic
    /// predicate (PostGIS <c>ST_DWithin</c> on a <c>geography</c> cast) for full spheroidal
    /// accuracy and to push the computation off this process.
    /// </summary>
    Task<bool> IsWithinDistanceAsync(string featureId, GeoPoint point, double toleranceMeters)
    {
        var feature = GetById(featureId);
        if (feature?.Geometry is null) return Task.FromResult(false);

        return Task.FromResult(Services.GeodeticDistanceCalculator.IsWithinDistance(feature.Geometry.Centroid, point, toleranceMeters));
    }

    // ── Topology queries (graph-backed) ───────────────────────────────────────
    //
    // Default implementations delegate to the static TopoGraph helper over GetAll() —
    // every in-memory-cache-backed provider's GetAll() already returns the exact
    // collection these once forwarded to explicitly, so this is behavior-preserving
    // (XD01-135). Override only if a provider can push traversal server-side.

    /// <summary>Returns the direct downstream neighbors of <paramref name="featureId"/>.</summary>
    IReadOnlyList<GeoFeature> GetNeighbors(string featureId) =>
        TopoGraph.GetNeighbors(featureId, GetAll());

    /// <summary>
    /// Returns the direct downstream neighbors of <paramref name="featureId"/> filtered by
    /// optional <paramref name="edgeKind"/> and <paramref name="targetAssetTypeId"/>.
    /// </summary>
    IReadOnlyList<GeoFeature> GetNeighbors(string featureId, string? edgeKind, string? targetAssetTypeId = null) =>
        TopoGraph.GetNeighbors(featureId, GetAll(), edgeKind, targetAssetTypeId);

    /// <summary>Returns all features reachable from <paramref name="featureId"/> (BFS forward).</summary>
    IReadOnlyList<GeoFeature> GetDescendants(string featureId) =>
        TopoGraph.GetDescendants(featureId, GetAll());

    /// <summary>
    /// Returns all features reachable from <paramref name="featureId"/> (BFS forward) filtered by
    /// optional <paramref name="edgeKind"/> and <paramref name="targetAssetTypeId"/>.
    /// </summary>
    IReadOnlyList<GeoFeature> GetDescendants(string featureId, string? edgeKind, string? targetAssetTypeId = null) =>
        TopoGraph.GetDescendants(featureId, GetAll(), edgeKind, targetAssetTypeId);

    /// <summary>Returns all features that can reach <paramref name="featureId"/> (BFS reverse).</summary>
    IReadOnlyList<GeoFeature> GetAncestors(string featureId) =>
        TopoGraph.GetAncestors(featureId, GetAll());

    /// <summary>BFS shortest path (fewest hops) between two features. Empty when unreachable.</summary>
    IReadOnlyList<GeoFeature> FindPath(string fromId, string toId) =>
        TopoGraph.FindPath(fromId, toId, GetAll());

    /// <summary>
    /// Dijkstra shortest path (minimum total <see cref="TopoEdge.Weight"/>) between two features.
    /// Empty when unreachable.
    /// </summary>
    IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId) =>
        TopoGraph.FindShortestPath(fromId, toId, GetAll());

    /// <summary>
    /// Groups all features into weakly connected components (edge direction ignored).
    /// Isolated features each form their own single-element component.
    /// </summary>
    IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents() =>
        TopoGraph.GetConnectedComponents(GetAll());

    /// <summary>Returns <c>true</c> when the directed topology graph contains at least one cycle.</summary>
    bool HasCycles() => TopoGraph.HasCycles(GetAll());

    /// <summary>
    /// Returns features in topological (process) order — sources first.
    /// Throws <see cref="InvalidOperationException"/> when the graph contains cycles.
    /// </summary>
    IReadOnlyList<GeoFeature> TopologicalSort() => TopoGraph.TopologicalSort(GetAll());

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

    /// <summary>
    /// The asset types carrying <paramref name="role"/> (<see cref="AssetType.Roles"/>, ordinal),
    /// in <see cref="GetAssetTypes"/> order (XD01-367). Provider-agnostic: the default filters
    /// <see cref="GetAssetTypes"/>, so every provider — including those whose types carry no
    /// roles — answers it.
    /// </summary>
    IReadOnlyList<AssetType> GetAssetTypesWithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return [.. GetAssetTypes().Where(t => AssetTypeRoles.HasRole(t, role))];
    }

    void AddAssetType(AssetType assetType);
    void DeleteAssetType(Guid id);

    // Layer management
    IReadOnlyList<Layer> GetLayers();
    void AddLayer(Layer layer);
    void DeleteLayer(Guid id);

    // Layer rule management
    IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId);
    void AddLayerRule(LayerRule layerRule);
    void DeleteLayerRule(Guid id);

    event EventHandler<GeoFeature>? FeatureAdded;
    event EventHandler<GeoFeature>? FeatureUpdated;
    event EventHandler<string>? FeatureDeleted;
    event EventHandler? CollectionChanged;
}
