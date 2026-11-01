namespace GeoAssets.FieldService.Server;

internal sealed record InterruptionEventDto(string ConsumerUnitId, DateTimeOffset StartedAt, DateTimeOffset EndedAt);
