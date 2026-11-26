using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// The pass-1 group-anchor index, in the source's native (projected, metre) CRS: anchor candidates in one STRtree per
/// <see cref="Profiles.LayerDefinition.AnchorNamespace"/>, and each group's nodes as compact points, keyed by
/// (target namespace, group key).
/// </summary>
internal sealed class AnchorIndex
{
    private readonly Dictionary<string, STRtree<IndexEntry>> _candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<IndexKey, List<AnchorNode>> _groups = [];

    public void AddCandidate(string @namespace, IndexEntry entry)
    {
        if (!_candidates.TryGetValue(@namespace, out var tree))
        {
            tree = new STRtree<IndexEntry>();
            _candidates.Add(@namespace, tree);
        }

        tree.Insert(new Envelope(entry.X, entry.X, entry.Y, entry.Y), entry);
    }

    public void AddNode(IndexKey group, AnchorNode node)
    {
        if (!_groups.TryGetValue(group, out var nodes))
        {
            nodes = [];
            _groups.Add(group, nodes);
        }

        nodes.Add(node);
    }

    /// <summary>The distinct candidates of the group's namespace within <paramref name="tolerance"/> of any of its nodes.</summary>
    public AnchorMatch Match(IndexKey group, double tolerance)
    {
        if (!_groups.TryGetValue(group, out var nodes))
        {
            return new AnchorMatch(0, default, double.NaN, 0);
        }

        if (!_candidates.TryGetValue(group.Namespace, out var tree))
        {
            return new AnchorMatch(0, default, double.NaN, nodes.Count);
        }

        var nearest = new Dictionary<Guid, (IndexEntry Entry, double Distance)>();
        foreach (var node in nodes)
        {
            foreach (var candidate in tree.Query(new Envelope(node.X - tolerance, node.X + tolerance, node.Y - tolerance, node.Y + tolerance)))
            {
                var distance = candidate.DistanceTo(node.X, node.Y);
                if (distance <= tolerance
                    && (!nearest.TryGetValue(candidate.FeatureId, out var known) || distance < known.Distance))
                {
                    nearest[candidate.FeatureId] = (candidate, distance);
                }
            }
        }

        if (nearest.Count != 1)
        {
            return new AnchorMatch(nearest.Count, default, double.NaN, nodes.Count);
        }

        var (anchor, anchorDistance) = nearest.Values.First();
        return new AnchorMatch(1, anchor, anchorDistance, nodes.Count);
    }
}
