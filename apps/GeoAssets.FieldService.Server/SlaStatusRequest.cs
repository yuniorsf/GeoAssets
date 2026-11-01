namespace GeoAssets.FieldService.Server;

/// <summary>Request shape shared by the Electrical SLA-clock and Sanitation response-SLA endpoints — both wrap the same stateless deadline-clock computation, just against each archetype's own domain type.</summary>
internal sealed record SlaStatusRequest(DateTimeOffset StartedAt, TimeSpan Deadline, DateTimeOffset AsOf, double AtRiskThreshold = 0.8);
