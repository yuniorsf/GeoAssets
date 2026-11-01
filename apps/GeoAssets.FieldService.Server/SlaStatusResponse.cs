namespace GeoAssets.FieldService.Server;

internal sealed record SlaStatusResponse(string Status, TimeSpan Remaining, double PercentElapsed, DateTimeOffset DueAt);
