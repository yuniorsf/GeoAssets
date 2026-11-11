namespace GeoAssets.FieldService.Server;

/// <summary>Response body for <c>GET /api/geoassets/dispatch/advisory-plan</c> (XD01-256).</summary>
internal sealed record DynamicDispatchAdvisoryResponse(
    IReadOnlyList<DynamicDispatchItineraryDto> SuggestedItineraries,
    IReadOnlyList<string> RankedOrderIds,
    IReadOnlyList<string> DeadlineCriticalOrderIds,
    IReadOnlyList<string> UnassignedOrderIds,
    IReadOnlyList<string> SkippedNoLocationOrderIds,
    double OptimizedDistanceMeters,
    double BaselineDistanceMeters
);
