namespace GeoAssets.Workflow.FieldWork;

/// <summary>How <see cref="ThreeWayConflictReconciler"/> categorized one reconciliation attempt.</summary>
public enum ConflictClassification
{
    /// <summary>The device made no offline changes at all — the server's current state is adopted as-is.</summary>
    TrivialCatchUp,

    /// <summary>Both sides diverged from the common base, but not in a way that collides on status or assignment — auto-merged.</summary>
    BenignDivergence,

    /// <summary>
    /// The device completed the work while the server independently moved away from the common
    /// base (reassigned, or changed status to anything other than also-Completed) — never
    /// auto-merged; escalated to <see cref="FieldWorkStatus.ConflictReview"/> for a human.
    /// </summary>
    StructuralCollision,
}
