namespace GeoAssets.Workflow.Targets;

/// <summary>
/// How an order type reacts when one of its targets is already a target of another open order.
/// </summary>
public enum OrderTargetExclusivity
{
    /// <summary>Overlapping targets are allowed and not reported.</summary>
    None,

    /// <summary>Overlapping targets are allowed but flagged as a warning.</summary>
    WarnOnOverlap,

    /// <summary>Overlapping targets are rejected.</summary>
    BlockOnOverlap,
}
