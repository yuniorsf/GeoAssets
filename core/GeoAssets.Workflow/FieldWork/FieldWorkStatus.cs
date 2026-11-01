namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// String state keys for the field-crew dispatch lifecycle (XD01-220) — the
/// <see cref="GeoAssets.Workflow.Orders.OrderType.States"/>/<see cref="GeoAssets.Workflow.Orders.OrderType.Transitions"/>
/// graph <see cref="FieldWorkWorkflowGraph"/> builds. Deliberately its own, fully independent set
/// of constants rather than reusing <c>ServiceOrderStatus</c>'s — some names coincide (e.g.
/// <see cref="Draft"/>, <see cref="Completed"/>) but carry different graph semantics here (e.g.
/// <see cref="Completed"/> is not a success-terminal state in this graph; <see cref="Inspected"/>
/// is), so keeping them textually distinct avoids a reader assuming shared meaning from a shared
/// constant.
/// </summary>
public static class FieldWorkStatus
{
    /// <summary>Created but not yet submitted for scheduling.</summary>
    public const string Draft = "Draft";

    /// <summary>Submitted and waiting to be scheduled.</summary>
    public const string Pending = "Pending";

    /// <summary>A crew and time slot have been assigned.</summary>
    public const string Scheduled = "Scheduled";

    /// <summary>Formally dispatched to the assigned crew.</summary>
    public const string Dispatched = "Dispatched";

    /// <summary>The crew has departed and is traveling to the site.</summary>
    public const string EnRoute = "EnRoute";

    /// <summary>The crew has arrived at the site (gated by a geofence arrival check — XD01-221).</summary>
    public const string OnSite = "OnSite";

    /// <summary>Field work is actively being executed (gated by a safety-gate precondition — XD01-222).</summary>
    public const string InProgress = "InProgress";

    /// <summary>Work is temporarily paused on-site, waiting for an external condition.</summary>
    public const string Suspended = "Suspended";

    /// <summary>
    /// The crew has finished the physical work, pending review — not itself a success-terminal
    /// state in this graph (compare <see cref="Inspected"/>).
    /// </summary>
    public const string Completed = "Completed";

    /// <summary>Reviewed and accepted as correct — this graph's success-terminal state.</summary>
    public const string Inspected = "Inspected";

    /// <summary>
    /// Reviewed and flagged with a discrepancy requiring reconciliation (XD01-223's deterministic
    /// 3-way offline conflict reconciliation engine owns what happens next).
    /// </summary>
    public const string ConflictReview = "ConflictReview";

    /// <summary>Cancelled before the crew committed to on-site work.</summary>
    public const string Cancelled = "Cancelled";
}
