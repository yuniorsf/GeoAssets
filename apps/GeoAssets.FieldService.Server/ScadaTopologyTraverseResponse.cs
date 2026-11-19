namespace GeoAssets.FieldService.Server;

/// <summary>Response body for <c>POST /api/geoassets/topology/traverse</c> (XD01-239).</summary>
internal sealed record ScadaTopologyTraverseResponse(
    string ServiceOrderId,
    IReadOnlyList<string> ImpactedFeatureIds,
    IReadOnlyList<string> CriticalFacilityFeatureIds,
    string? DispatchedCrewFeatureId,
    /// <summary>True when the core asset source was unreachable for part of the dispatch
    /// (XD01-386) — the order exists but its impact list or crew dispatch is incomplete.</summary>
    bool IsDegraded,
    string? DegradedReason
);
