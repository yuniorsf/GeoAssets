namespace GeoAssets.Integrations.Scada;

/// <summary>Outcome of <see cref="IFeederOutageDispatchService.DispatchForBreakerTripAsync"/>.</summary>
public sealed record FeederOutageDispatchResult(
    /// <summary>ID of the auto-created emergency order, or <c>null</c> when none could be created
    /// because the tripped switch itself could not be read from the core (always with
    /// <see cref="IsDegraded"/>) — the caller should retry the alarm rather than treat it as handled.</summary>
    string? ServiceOrderId,
    string TrippedSwitchFeatureId,
    IReadOnlyList<string> ImpactedFeatureIds,
    IReadOnlyList<string> CriticalFacilityFeatureIds,
    /// <summary>ID of the nearest qualified crew feature the order was dispatched to, or
    /// <c>null</c> when no qualified crew was found within the search radius — the order is
    /// still created (so the outage isn't silently dropped) but is left undispatched.</summary>
    string? DispatchedCrewFeatureId,
    /// <summary>True when the core asset source was unreachable for part of the dispatch
    /// (XD01-386): the switch lookup (no order created), the downstream traversal (order created
    /// with no impacted features) or the crew lookup (order created, left undispatched).</summary>
    bool IsDegraded = false,
    /// <summary>Which step degraded and why, when <see cref="IsDegraded"/>; otherwise <c>null</c>.</summary>
    string? DegradedReason = null
);
