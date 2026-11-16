namespace GeoAssets.Workflow.Scada;

/// <summary>Outcome of <see cref="IFeederOutageDispatchService.DispatchForBreakerTripAsync"/>.</summary>
public sealed record FeederOutageDispatchResult(
    string ServiceOrderId,
    string TrippedSwitchFeatureId,
    IReadOnlyList<string> ImpactedFeatureIds,
    IReadOnlyList<string> CriticalFacilityFeatureIds,

    /// <summary>ID of the nearest qualified crew feature the order was dispatched to, or
    /// <c>null</c> when no qualified crew was found within the search radius — the order is
    /// still created (so the outage isn't silently dropped) but is left undispatched.</summary>
    string? DispatchedCrewFeatureId
);
