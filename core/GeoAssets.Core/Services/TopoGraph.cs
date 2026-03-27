using GeoAssets.Core.Models;

namespace GeoAssets.Core.Services;

/// <summary>
/// Static graph algorithms over a flat collection of <see cref="GeoFeature"/> objects
/// whose directed edges are expressed via <see cref="GeoFeature.Topology"/>.
///
/// Edge direction convention: owner → <see cref="TopoEdge.TargetId"/> (source → downstream).
/// Dangling edges (unknown TargetId) are silently ignored.
/// </summary>
public static class TopoGraph
{
    // ── Internal helpers ──────────────────────────────────────────────────────

    private static Dictionary<string, GeoFeature> Index(IEnumerable<GeoFeature> features) =>
        features.ToDictionary(f => f.Id);

    /// <summary>Forward adjacency: id → list of reachable ids.</summary>
    private static Dictionary<string, List<string>> Forward(
        IReadOnlyCollection<GeoFeature> features,
        Dictionary<string, GeoFeature>  index)
    {
        var adj = features.ToDictionary(f => f.Id, _ => new List<string>());
        foreach (var f in features)
            foreach (var e in f.Topology)
                if (index.ContainsKey(e.TargetId))
                    adj[f.Id].Add(e.TargetId);
        return adj;
    }

    /// <summary>Reverse adjacency: id → list of predecessors.</summary>
    private static Dictionary<string, List<string>> Reverse(
        IReadOnlyCollection<GeoFeature> features,
        Dictionary<string, GeoFeature>  index)
    {
        var rev = features.ToDictionary(f => f.Id, _ => new List<string>());
        foreach (var f in features)
            foreach (var e in f.Topology)
                if (index.ContainsKey(e.TargetId))
                    rev[e.TargetId].Add(f.Id);
        return rev;
    }

    // ── Neighbor / reachability ───────────────────────────────────────────────

    /// <summary>Returns the direct downstream neighbors of <paramref name="featureId"/>.</summary>
    public static IReadOnlyList<GeoFeature> GetNeighbors(
        string featureId, IEnumerable<GeoFeature> features)
    {
        var idx = Index(features);
        return idx.TryGetValue(featureId, out var src)
            ? [.. src.Topology.Where(e => idx.ContainsKey(e.TargetId)).Select(e => idx[e.TargetId])]
            : [];
    }

    /// <summary>
    /// Returns all features reachable from <paramref name="featureId"/> following
    /// edges forward (BFS). The starting feature itself is excluded.
    /// </summary>
    public static IReadOnlyList<GeoFeature> GetDescendants(
        string featureId, IEnumerable<GeoFeature> features)
    {
        var all = features.ToList();
        var idx = Index(all);
        if (!idx.ContainsKey(featureId)) return [];

        var fwd     = Forward(all, idx);
        var visited = new HashSet<string>();
        var queue   = new Queue<string>([featureId]);

        while (queue.TryDequeue(out var id))
            foreach (var nb in fwd[id])
                if (visited.Add(nb))
                    queue.Enqueue(nb);

        return [.. visited.Select(id => idx[id])];
    }

    /// <summary>
    /// Returns all features that can reach <paramref name="featureId"/> following
    /// edges in reverse (BFS). The starting feature itself is excluded.
    /// </summary>
    public static IReadOnlyList<GeoFeature> GetAncestors(
        string featureId, IEnumerable<GeoFeature> features)
    {
        var all = features.ToList();
        var idx = Index(all);
        if (!idx.ContainsKey(featureId)) return [];

        var rev     = Reverse(all, idx);
        var visited = new HashSet<string>();
        var queue   = new Queue<string>([featureId]);

        while (queue.TryDequeue(out var id))
            foreach (var pred in rev[id])
                if (visited.Add(pred))
                    queue.Enqueue(pred);

        return [.. visited.Select(id => idx[id])];
    }

    // ── Topological sort ──────────────────────────────────────────────────────

    /// <summary>
    /// Kahn's algorithm — returns features in topological (process) order, sources first.
    /// Isolated nodes are included at the front in arbitrary order.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the graph contains cycles.</exception>
    public static IReadOnlyList<GeoFeature> TopologicalSort(IEnumerable<GeoFeature> features)
    {
        var all = features.ToList();
        var idx = Index(all);
        var fwd = Forward(all, idx);

        var inDegree = all.ToDictionary(f => f.Id, _ => 0);
        foreach (var f in all)
            foreach (var nb in fwd[f.Id])
                inDegree[nb]++;

        var queue  = new Queue<string>(all.Where(f => inDegree[f.Id] == 0).Select(f => f.Id));
        var result = new List<GeoFeature>(all.Count);

        while (queue.TryDequeue(out var id))
        {
            result.Add(idx[id]);
            foreach (var nb in fwd[id])
                if (--inDegree[nb] == 0)
                    queue.Enqueue(nb);
        }

        if (result.Count != all.Count)
            throw new InvalidOperationException(
                "Topological sort failed: the graph contains one or more directed cycles.");

        return result;
    }

    // ── Path finding ──────────────────────────────────────────────────────────

    /// <summary>
    /// BFS shortest path (fewest hops) from <paramref name="fromId"/> to <paramref name="toId"/>.
    /// Returns an empty list when no directed path exists.
    /// </summary>
    public static IReadOnlyList<GeoFeature> FindPath(
        string fromId, string toId, IEnumerable<GeoFeature> features)
    {
        var all = features.ToList();
        var idx = Index(all);
        if (!idx.ContainsKey(fromId) || !idx.ContainsKey(toId)) return [];
        if (fromId == toId) return [idx[fromId]];

        var fwd  = Forward(all, idx);
        var prev = new Dictionary<string, string?> { [fromId] = null };
        var queue = new Queue<string>([fromId]);

        while (queue.TryDequeue(out var id))
        {
            if (id == toId) break;
            foreach (var nb in fwd[id])
                if (!prev.ContainsKey(nb))
                {
                    prev[nb] = id;
                    queue.Enqueue(nb);
                }
        }

        if (!prev.ContainsKey(toId)) return [];
        return ReconstructPath(toId, prev, idx);
    }

    /// <summary>
    /// Dijkstra shortest path (minimum cumulative <see cref="TopoEdge.Weight"/>) from
    /// <paramref name="fromId"/> to <paramref name="toId"/>.
    /// Returns an empty list when no directed path exists.
    /// </summary>
    public static IReadOnlyList<GeoFeature> FindShortestPath(
        string fromId, string toId, IEnumerable<GeoFeature> features)
    {
        var all = features.ToList();
        var idx = Index(all);
        if (!idx.ContainsKey(fromId) || !idx.ContainsKey(toId)) return [];
        if (fromId == toId) return [idx[fromId]];

        var dist = all.ToDictionary(f => f.Id, _ => double.PositiveInfinity);
        var prev = new Dictionary<string, string?> { [fromId] = null };
        dist[fromId] = 0;

        var pq = new PriorityQueue<string, double>();
        pq.Enqueue(fromId, 0);

        while (pq.TryDequeue(out var id, out var priority))
        {
            if (priority > dist[id]) continue; // stale entry
            if (id == toId) break;

            foreach (var edge in idx[id].Topology)
            {
                if (!dist.ContainsKey(edge.TargetId)) continue;
                var next = dist[id] + edge.Weight;
                if (next < dist[edge.TargetId])
                {
                    dist[edge.TargetId] = next;
                    prev[edge.TargetId] = id;
                    pq.Enqueue(edge.TargetId, next);
                }
            }
        }

        if (!prev.ContainsKey(toId)) return [];
        return ReconstructPath(toId, prev, idx);
    }

    // ── Connected components ──────────────────────────────────────────────────

    /// <summary>
    /// Groups features into weakly connected components (edge direction ignored).
    /// Isolated nodes each form their own single-element component.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents(
        IEnumerable<GeoFeature> features)
    {
        var all = features.ToList();
        var idx = Index(all);

        // Build undirected adjacency
        var undirected = all.ToDictionary(f => f.Id, _ => new HashSet<string>());
        foreach (var f in all)
            foreach (var e in f.Topology)
            {
                if (!undirected.ContainsKey(e.TargetId)) continue;
                undirected[f.Id].Add(e.TargetId);
                undirected[e.TargetId].Add(f.Id);
            }

        var visited    = new HashSet<string>();
        var components = new List<IReadOnlyList<GeoFeature>>();

        foreach (var f in all)
        {
            if (!visited.Add(f.Id)) continue;

            var component = new List<GeoFeature>();
            var queue     = new Queue<string>([f.Id]);

            while (queue.TryDequeue(out var id))
            {
                component.Add(idx[id]);
                foreach (var nb in undirected[id])
                    if (visited.Add(nb))
                        queue.Enqueue(nb);
            }

            components.Add(component);
        }

        return components;
    }

    // ── Cycle detection ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the directed graph contains at least one cycle.
    /// Uses DFS with three-colour marking (white → grey → black).
    /// </summary>
    public static bool HasCycles(IEnumerable<GeoFeature> features)
    {
        var all   = features.ToList();
        var idx   = Index(all);
        var fwd   = Forward(all, idx);
        var color = all.ToDictionary(f => f.Id, _ => 0); // 0=white 1=grey 2=black

        bool Dfs(string id)
        {
            color[id] = 1;
            foreach (var nb in fwd[id])
            {
                if (color[nb] == 1) return true; // back-edge → cycle
                if (color[nb] == 0 && Dfs(nb)) return true;
            }
            color[id] = 2;
            return false;
        }

        return all.Any(f => color[f.Id] == 0 && Dfs(f.Id));
    }

    // ── Private utils ─────────────────────────────────────────────────────────

    private static IReadOnlyList<GeoFeature> ReconstructPath(
        string target,
        Dictionary<string, string?> prev,
        Dictionary<string, GeoFeature> idx)
    {
        var path = new List<GeoFeature>();
        for (var cur = (string?)target; cur is not null; cur = prev.GetValueOrDefault(cur))
            path.Add(idx[cur]);
        path.Reverse();
        return path;
    }
}
