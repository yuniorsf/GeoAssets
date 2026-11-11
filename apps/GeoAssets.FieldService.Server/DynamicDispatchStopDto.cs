namespace GeoAssets.FieldService.Server;

/// <summary>One ordered stop within a <see cref="DynamicDispatchItineraryDto"/> (XD01-256).</summary>
internal sealed record DynamicDispatchStopDto(string OrderId, int SequenceIndex, DateTimeOffset EstimatedArrival);
