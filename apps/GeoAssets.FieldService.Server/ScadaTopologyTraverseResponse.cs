namespace GeoAssets.FieldService.Server;

/// <summary>Response body for <c>POST /api/geoassets/topology/traverse</c> (XD01-239).</summary>
internal sealed record ScadaTopologyTraverseResponse(
    string ServiceOrderId,
    IReadOnlyList<string> ImpactedFeatureIds,
    IReadOnlyList<string> CriticalFacilityFeatureIds,
    string? DispatchedCrewFeatureId
);
