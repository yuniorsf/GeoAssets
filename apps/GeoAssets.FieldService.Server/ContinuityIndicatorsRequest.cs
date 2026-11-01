namespace GeoAssets.FieldService.Server;

internal sealed record ContinuityIndicatorsRequest(
    string ConsumerUnitId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEndExclusive,
    List<InterruptionEventDto> Events,
    TimeSpan? MinimumQualifyingDuration = null);
