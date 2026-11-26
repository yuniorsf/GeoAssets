using System.Globalization;
using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Ingestion.Projection;

/// <summary>
/// Builds the <see cref="TopoEdge"/> of a resolved reference (weight 1, metadata <c>resolution</c> and, for snaps and
/// anchors, <c>distance</c>): Start/End → <c>connected-to</c> with <c>terminal</c> and <c>node</c>; Location →
/// <c>mounted-on</c>; Connection → <c>connected-to</c> with <c>node</c>; MemberOf → <c>member-of</c> with
/// <c>group</c>; FedBy → <c>feeds</c>, owned by the target and pointing back at the declaring element.
/// </summary>
internal static class TopologyEdgeFactory
{
    public const string ConnectedTo = "connected-to";
    public const string MountedOn = "mounted-on";
    public const string MemberOf = "member-of";
    public const string Feeds = "feeds";

    public static TopoEdge Forward(ResolvedReference resolved)
    {
        var reference = resolved.Reference;
        var edge = reference.Role switch
        {
            ReferenceRole.Location => Create(resolved.TargetFeatureId, MountedOn, resolved),
            ReferenceRole.MemberOf => Create(resolved.TargetFeatureId, MemberOf, resolved),
            _ => Create(resolved.TargetFeatureId, ConnectedTo, resolved),
        };

        switch (reference.Role)
        {
            case ReferenceRole.Start:
                edge.Metadata["terminal"] = "1";
                AddNode(edge, reference.LocalNode);
                break;
            case ReferenceRole.End:
                edge.Metadata["terminal"] = "2";
                AddNode(edge, reference.LocalNode);
                break;
            case ReferenceRole.Connection:
                AddNode(edge, reference.LocalNode);
                break;
            case ReferenceRole.MemberOf when reference.Qualifier is not null:
                edge.Metadata["group"] = reference.Qualifier;
                break;
            default:
                break;
        }

        return edge;
    }

    /// <summary>The reversed edge of a FedBy reference: owned by the target, pointing at <paramref name="fedFeatureId"/>.</summary>
    public static TopoEdge FeedsEdge(ResolvedReference resolved, string fedFeatureId) => Create(fedFeatureId, Feeds, resolved);

    private static TopoEdge Create(string targetId, string kind, ResolvedReference resolved)
    {
        var edge = new TopoEdge { TargetId = targetId, Kind = kind, Weight = 1.0 };
        edge.Metadata["resolution"] = ResolutionMethodLabels.For(resolved.Method);
        if ((resolved.Method is ResolutionMethod.Snap or ResolutionMethod.Anchor) && resolved.DistanceMeters is { } distance)
        {
            edge.Metadata["distance"] = distance.ToString("F2", CultureInfo.InvariantCulture);
        }

        return edge;
    }

    private static void AddNode(TopoEdge edge, int? localNode)
    {
        if (localNode is { } node)
        {
            edge.Metadata["node"] = node.ToString(CultureInfo.InvariantCulture);
        }
    }
}
