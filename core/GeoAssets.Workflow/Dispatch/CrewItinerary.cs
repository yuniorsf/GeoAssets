namespace GeoAssets.Workflow.Dispatch;

/// <summary>
/// A single crew's routed set of stops for one dispatch pass, plus the total travel distance
/// the route covers — the figure <see cref="ICrewRoutingEngine"/> callers compare against a
/// naive-nearest-neighbor baseline to satisfy XD01-240's "measurably shorter itinerary" AC.
/// </summary>
public sealed record CrewItinerary(
    string CrewFeatureId,
    IReadOnlyList<CrewItineraryStop> Stops,
    double TotalDistanceMeters);
