namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>Direction of topology traversal.</summary>
public enum TraversalDirection
{
    /// <summary>Follow edges forward (children, descendants).</summary>
    Downstream,

    /// <summary>Follow edges backward (parents, ancestors).</summary>
    Upstream,

    /// <summary>Traverse in both directions.</summary>
    Both
}
