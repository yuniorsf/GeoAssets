namespace GeoAssets.Workflow.Dispatch;

/// <summary>
/// Builds a crew's stop sequence for a set of orders already known to be assignable to that
/// crew (skill/equipment eligibility is the caller's concern — see
/// <c>GeoAssets.Workflow.Agents.Dispatch.CrewSkillMatcher</c>). Implementations route for
/// a single crew/depot at a time so a caller can run eligible-crew selection and clustering
/// (feeder/pressure-zone grouping, XD01-240) however it needs to upstream of this interface.
/// </summary>
public interface ICrewRoutingEngine
{
    /// <param name="depotLocation">Crew's start/end point for the itinerary.</param>
    /// <param name="orders">Orders already filtered to ones this crew is eligible for.</param>
    /// <param name="shiftStart">Clock time the crew's route begins — used to compute each
    /// stop's <see cref="CrewItineraryStop.EstimatedArrival"/>.</param>
    CrewItinerary BuildItinerary(
        string crewFeatureId,
        Core.Models.Geometry.GeoPoint depotLocation,
        IReadOnlyList<DispatchableOrder> orders,
        DateTimeOffset shiftStart);
}
