namespace GeoAssets.Workflow.Dispatch;

/// <summary>One ordered stop within a <see cref="CrewItinerary"/> (XD01-240).</summary>
public sealed record CrewItineraryStop(string OrderId, int SequenceIndex, DateTimeOffset EstimatedArrival);
