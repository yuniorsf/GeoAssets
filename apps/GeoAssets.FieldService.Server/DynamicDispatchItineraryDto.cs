namespace GeoAssets.FieldService.Server;

/// <summary>One crew's suggested itinerary within a <see cref="DynamicDispatchAdvisoryResponse"/> (XD01-256).</summary>
internal sealed record DynamicDispatchItineraryDto(
    string CrewFeatureId,
    IReadOnlyList<DynamicDispatchStopDto> Stops,
    double TotalDistanceMeters
);
