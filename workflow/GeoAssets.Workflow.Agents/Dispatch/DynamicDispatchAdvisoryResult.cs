using GeoAssets.Workflow.Dispatch;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// Output of one <see cref="DynamicCrewDispatchAdvisorService.GenerateAdvisoryPlanAsync"/> pass
/// (XD01-256) — a suggestion for a human dispatcher to review, override, or discard. Nothing in
/// this type or the service that produces it dispatches an order; that stays the dispatcher's
/// own decision (this ticket's AC: "advisory/overridable... not auto-committed without review").
/// <see cref="OptimizedDistanceMeters"/> vs <see cref="BaselineDistanceMeters"/> is the same
/// routed-vs-naive-nearest-neighbor comparison XD01-240 measures its own itineraries against,
/// reused here to satisfy this ticket's own "measurably reduce crew travel distance" AC.
/// </summary>
public sealed record DynamicDispatchAdvisoryResult(
    IReadOnlyList<CrewItinerary> SuggestedItineraries,
    IReadOnlyList<string> RankedOrderIds,
    IReadOnlyList<string> DeadlineCriticalOrderIds,
    IReadOnlyList<string> UnassignedOrderIds,
    IReadOnlyList<string> SkippedNoLocationOrderIds,
    double OptimizedDistanceMeters,
    double BaselineDistanceMeters);
