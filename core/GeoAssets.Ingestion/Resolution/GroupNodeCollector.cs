using GeoAssets.Ingestion.Model;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// Pass 1 for a <see cref="Profiles.LayerDefinition.CollectsGroupAnchors"/> element: for each group of its group-anchor
/// references, adds the element's nodes — the <see cref="ElementReference.SnapPoint"/>s of its Start/End references,
/// or, where it has no Start (End) snap point, the geometry's first (last) coordinate.
/// </summary>
internal static class GroupNodeCollector
{
    public static void Collect(AnchorIndex anchors, CanonicalElement element)
    {
        List<IndexKey>? groups = null;
        foreach (var reference in element.References)
        {
            if (GroupAnchorKey.TryGet(reference, out var group) && !(groups ??= []).Contains(group))
            {
                groups.Add(group);
            }
        }

        if (groups is null)
        {
            return;
        }

        var nodes = NodesOf(element);
        foreach (var group in groups)
        {
            foreach (var node in nodes)
            {
                anchors.AddNode(group, node);
            }
        }
    }

    private static List<AnchorNode> NodesOf(CanonicalElement element)
    {
        var nodes = new List<AnchorNode>(2);
        var hasStart = AddSnapPoints(nodes, element.References, ReferenceRole.Start);
        var hasEnd = AddSnapPoints(nodes, element.References, ReferenceRole.End);
        if (!hasStart && TryGetEndpoint(element.Geometry, first: true, out var start))
        {
            nodes.Add(start);
        }

        if (!hasEnd && TryGetEndpoint(element.Geometry, first: false, out var end))
        {
            nodes.Add(end);
        }

        return nodes;
    }

    private static bool AddSnapPoints(List<AnchorNode> nodes, IReadOnlyList<ElementReference> references, ReferenceRole role)
    {
        var added = false;
        foreach (var reference in references)
        {
            if (reference.Role == role && reference.SnapPoint is { IsEmpty: false } point && IsFinite(point.X, point.Y))
            {
                nodes.Add(new AnchorNode(point.X, point.Y));
                added = true;
            }
        }

        return added;
    }

    private static bool TryGetEndpoint(Geometry geometry, bool first, out AnchorNode node)
    {
        node = default;
        if (geometry.IsEmpty)
        {
            return false;
        }

        double x;
        double y;
        if (geometry is LineString line)
        {
            var sequence = line.CoordinateSequence;
            var index = first ? 0 : sequence.Count - 1;
            x = sequence.GetX(index);
            y = sequence.GetY(index);
        }
        else
        {
            var coordinates = geometry.Coordinates;
            var coordinate = first ? coordinates[0] : coordinates[^1];
            x = coordinate.X;
            y = coordinate.Y;
        }

        if (!IsFinite(x, y))
        {
            return false;
        }

        node = new AnchorNode(x, y);
        return true;
    }

    private static bool IsFinite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);
}
