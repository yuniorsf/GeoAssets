namespace GeoAssets.Workflow.Targets;

/// <summary>
/// How strongly one target of an order overlaps with other open orders, as last evaluated by
/// <see cref="TargetConflictEvaluator"/>. Declared weakest to strongest, so a greater value is
/// a stronger conflict.
/// </summary>
public enum OrderTargetConflictLevel
{
    /// <summary>No other open order outside this order's family holds the feature, or nobody cares.</summary>
    None,

    /// <summary>Another open order holds the feature; the overlap is allowed but flagged.</summary>
    Warning,

    /// <summary>Another open order holds the feature and the overlap is not allowed.</summary>
    Blocked,
}
