using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Resolution;

namespace GeoAssets.Ingestion.Projection;

/// <summary>
/// Assembles an element's outgoing edges: one per resolved reference, except FedBy, whose reversed <c>feeds</c> edge is
/// buffered for the target — or dropped with an info <see cref="ImportIssueCodes.DanglingReference"/> when the target
/// was already projected — plus the <c>feeds</c> edges other elements buffered for this one. Edges still buffered at
/// the end of the run (their owner was never projected) are dropped and reported the same way.
/// </summary>
internal sealed class TopologyAssembler(ElementRegistry registry, IssueLog issues)
{
    private readonly ReversedEdgeBuffer _reversed = new();

    public List<TopoEdge> Assemble(FeatureIdentity owner, SourceKey ownerKey, IReadOnlyList<ResolvedReference> resolved)
    {
        var edges = new List<TopoEdge>(resolved.Count);
        foreach (var reference in resolved)
        {
            if (reference.Reference.Role == ReferenceRole.FedBy)
            {
                Defer(reference, owner, ownerKey);
            }
            else
            {
                edges.Add(TopologyEdgeFactory.Forward(reference));
            }
        }

        edges.AddRange(_reversed.Take(owner.Text));
        return edges;
    }

    public void ReportUnattached()
    {
        foreach (var (ownerId, edges) in _reversed.Pending)
        {
            foreach (var edge in edges)
            {
                issues.Add(
                    IssueSeverity.Info,
                    ImportIssueCodes.DanglingReference,
                    edge.TargetId,
                    string.Concat("The feeding element ", ownerId, " was not imported in this run; its feeds edge was dropped."));
            }
        }
    }

    private void Defer(ResolvedReference reference, FeatureIdentity owner, SourceKey ownerKey)
    {
        if (registry.IsProjected(FeatureIdentity.FromText(reference.TargetFeatureId).Value))
        {
            issues.Add(
                IssueSeverity.Info,
                ImportIssueCodes.DanglingReference,
                ownerKey.ToString(),
                string.Concat("FedBy target ", reference.TargetFeatureId, " was already written; its feeds edge was dropped."));
            return;
        }

        _reversed.Add(reference.TargetFeatureId, TopologyEdgeFactory.FeedsEdge(reference, owner.Text));
    }
}
