namespace GeoAssets.Workflow.Orders;

/// <summary>What an <see cref="OrderActionOutcome"/> reports.</summary>
public enum OrderActionOutcomeKind
{
    /// <summary>The action was performed.</summary>
    Applied,

    /// <summary>The order does not exist (any more).</summary>
    NotFound,

    /// <summary>The order changed after it was read.</summary>
    ConcurrentModification,

    /// <summary>The rule chain refused the action (<see cref="OrderActionOutcome.Verdict"/>).</summary>
    Denied,

    /// <summary>The action does not lead from <see cref="OrderActionOutcome.From"/> to <see cref="OrderActionOutcome.To"/>.</summary>
    InvalidTransition,

    /// <summary>A handler-specific failure with a stable <see cref="OrderActionOutcome.Code"/>.</summary>
    Problem,
}
