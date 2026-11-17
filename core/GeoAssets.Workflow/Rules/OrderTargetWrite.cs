namespace GeoAssets.Workflow.Rules;

/// <summary>
/// The two kinds of change to an order's targets, which a dispatch recipient is allowed
/// differently (XD01-333) — see <see cref="ServiceOrderRules.EvaluateTargetWrite"/>.
/// </summary>
public enum OrderTargetWrite
{
    /// <summary>Recording the work on a target: its activities, status and note.</summary>
    RecordWork = 0,

    /// <summary>Adding targets to the order (a selection) or removing targets from it.</summary>
    AddOrRemove = 1,
}
