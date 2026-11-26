using GeoAssets.Core.Models;

namespace GeoAssets.Ingestion.Projection;

/// <summary>Reversed (<c>feeds</c>) edges waiting for their owner — the FedBy target — to be projected, keyed by its feature id.</summary>
internal sealed class ReversedEdgeBuffer
{
    private readonly Dictionary<string, List<TopoEdge>> _byOwner = new(StringComparer.Ordinal);

    public IEnumerable<KeyValuePair<string, List<TopoEdge>>> Pending => _byOwner;

    public void Add(string ownerFeatureId, TopoEdge edge)
    {
        if (!_byOwner.TryGetValue(ownerFeatureId, out var edges))
        {
            edges = [];
            _byOwner[ownerFeatureId] = edges;
        }

        edges.Add(edge);
    }

    /// <summary>Removes and returns the edges waiting for <paramref name="ownerFeatureId"/>.</summary>
    public IReadOnlyList<TopoEdge> Take(string ownerFeatureId) =>
        _byOwner.Remove(ownerFeatureId, out var edges) ? edges : [];
}
