namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// What handling one kernel reply did (XD01-383) — the <c>outcome</c> tag of <c>geoassets.saga.apply.outcomes</c>, a fixed,
/// vertical-agnostic set.
/// </summary>
public enum OccupancyApplySagaOutcome
{
    /// <summary>The reply was not this vertical's, or names a batch no saga here holds; nothing changed.</summary>
    Ignored = 0,

    /// <summary>The message or the saga was already handled; nothing changed.</summary>
    Duplicate = 1,

    /// <summary>Applied: the saga committed and the order moved to its approved status.</summary>
    Committed = 2,

    /// <summary>Rejected: the saga reverted the occupancy and recorded the reason.</summary>
    Reverted = 3,

    /// <summary>
    /// The kernel applied a batch whose saga had already reverted (an operator's retry raced a rejection): the catalog holds
    /// what the vertical rolled back. Logged as an error for an operator to reconcile; nothing changed here.
    /// </summary>
    AppliedAfterRevert = 4,
}
